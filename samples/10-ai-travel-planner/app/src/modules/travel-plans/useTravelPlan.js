import { useEffect, useRef, useState } from 'react';
import { cancelTravelPlan, createTravelPlan, getTravelPlan } from './travelPlanApi.js';

const TERMINAL = new Set(['completed', 'partial', 'failed', 'cancelled']);

export function useTravelPlan() {
  const [job, setJob] = useState(null);
  const [snapshot, setSnapshot] = useState(null);
  const [error, setError] = useState('');
  const [expired, setExpired] = useState(false);
  const [creating, setCreating] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const generation = useRef(0);
  const createController = useRef(null);

  function reset() {
    generation.current += 1;
    createController.current?.abort();
    setJob(null);
    setSnapshot(null);
    setError('');
    setExpired(false);
    setCreating(false);
    setCancelling(false);
  }

  async function create(request) {
    reset();
    const current = generation.current;
    const controller = new AbortController();
    createController.current = controller;
    setCreating(true);
    try {
      const accepted = await createTravelPlan(request, controller.signal);
      if (current !== generation.current) return false;
      setJob(accepted);
      setSnapshot({ id: accepted.planId, status: accepted.status, eventId: 0, agentStatuses: {} });
      return true;
    } catch (cause) {
      if (current === generation.current && cause.name !== 'AbortError') setError(cause.message);
      return false;
    } finally {
      if (current === generation.current) setCreating(false);
    }
  }

  useEffect(() => {
    if (!job) return undefined;
    let active = true;
    let source;
    let timer;
    let polling = false;
    let stopped = false;
    const controller = new AbortController();

    function accept(next) {
      if (!active || next?.id !== job.planId) return;
      setSnapshot((previous) => !previous || next.eventId >= previous.eventId ? next : previous);
      if (TERMINAL.has(next.status)) {
        stopped = true;
        source?.close();
        window.clearTimeout(timer);
      }
    }

    async function reconcile() {
      try {
        const latest = await getTravelPlan(job.statusUrl, controller.signal);
        if (!active) return;
        accept(latest);
        setError('');
      } catch (cause) {
        if (!active || cause.name === 'AbortError') return;
        if (cause.expired) {
          stopped = true;
          setExpired(true);
          source?.close();
          window.clearTimeout(timer);
        }
        setError(cause.message);
      }
    }

    function schedulePoll() {
      if (polling || !active || stopped) return;
      polling = true;
      const poll = async () => {
        if (!active || stopped) return;
        await reconcile();
        if (active && !stopped && polling) timer = window.setTimeout(poll, 3000);
      };
      timer = window.setTimeout(poll, 3000);
    }

    function stopPoll() {
      polling = false;
      window.clearTimeout(timer);
    }

    void reconcile();
    if (typeof EventSource === 'undefined') {
      schedulePoll();
    } else {
      source = new EventSource(job.eventsUrl);
      source.onopen = () => { if (stopped) { source.close(); return; } stopPoll(); void reconcile(); };
      source.onerror = () => { if (!stopped) { void reconcile(); schedulePoll(); } };
      source.addEventListener('plan_status', (event) => {
        try { accept(JSON.parse(event.data)); } catch { void reconcile(); }
      });
      source.addEventListener('agent_status', (event) => {
        try {
          const update = JSON.parse(event.data);
          if (!active || update.planId !== job.planId) return;
          setSnapshot((previous) => previous && update.eventId > previous.eventId
            ? { ...previous, status: update.planStatus, eventId: update.eventId,
              agentStatuses: update.agentStatuses, agent: update.agent, agentStatus: update.status,
              updatedAt: update.updatedAt }
            : previous);
        } catch { void reconcile(); }
      });
      source.addEventListener('plan_completed', () => { void reconcile(); });
    }
    return () => {
      active = false;
      controller.abort();
      source?.close();
      window.clearTimeout(timer);
    };
  }, [job]);

  async function cancel() {
    if (!job || cancelling) return;
    const current = generation.current;
    setCancelling(true);
    try {
      const latest = await cancelTravelPlan(job.statusUrl);
      if (current === generation.current && latest?.id === job.planId) setSnapshot(latest);
    } catch (cause) {
      if (current === generation.current) {
        if (cause.expired) setExpired(true);
        setError(cause.message);
      }
    } finally {
      if (current === generation.current) setCancelling(false);
    }
  }

  return { job, snapshot, error, expired, creating, cancelling, create, cancel, reset };
}
