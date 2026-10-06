import { useEffect, useRef, useState } from 'react';

const starters = [
  { icon: '✳', title: 'Explore the menu', prompt: 'Show me the menu and recommend a few favorites.' },
  { icon: '♡', title: 'Find my kind of food', prompt: 'I’m vegetarian. What would you recommend?' },
  { icon: '↗', title: 'Start an order', prompt: 'Help me choose a meal for two and then check out with demo-card.' },
  { icon: '◷', title: 'Track an order', prompt: 'What is the status of my order?' },
];

function Icon({ name, className = '' }) {
  const common = { className, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 1.7, strokeLinecap: 'round', strokeLinejoin: 'round', 'aria-hidden': true };
  const paths = {
    leaf: <><path d="M20.5 3.5C12 3.5 5.2 5.7 4 11.1c-.8 3.7 2.1 6.3 5.4 5.6C15.1 15.5 18.5 9.6 20.5 3.5Z"/><path d="M3 21c3.4-5.8 7.1-8.9 12.8-12.1"/></>,
    send: <><path d="m22 2-7 20-4-9-9-4Z"/><path d="M22 2 11 13"/></>,
    plus: <><path d="M12 5v14M5 12h14"/></>,
    sparkle: <><path d="m12 3 1.7 5.3L19 10l-5.3 1.7L12 17l-1.7-5.3L5 10l5.3-1.7L12 3Z"/><path d="m19 16 .8 2.2L22 19l-2.2.8L19 22l-.8-2.2L16 19l2.2-.8L19 16Z"/></>,
    clock: <><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></>,
    arrow: <><path d="M7 17 17 7M7 7h10v10"/></>,
    close: <><path d="m6 6 12 12M18 6 6 18"/></>,
  };
  return <svg {...common}>{paths[name]}</svg>;
}

function initialMessage() {
  return [{ id: 'welcome', role: 'assistant', text: 'Hi there! I’m your table-side guide. I can help you explore the menu, plan an order, or check on a local demo order. What sounds good today?' }];
}

export default function App() {
  const [customerId, setCustomerId] = useState('guest-1');
  const [messages, setMessages] = useState(initialMessage);
  const [draft, setDraft] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [orderStatus, setOrderStatus] = useState('');
  const [order, setOrder] = useState(null);
  const [orderError, setOrderError] = useState('');
  const [history, setHistory] = useState([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState('');
  const [historyOpen, setHistoryOpen] = useState(false);
  const transcriptRef = useRef(null);
  const inputRef = useRef(null);

  useEffect(() => {
    transcriptRef.current?.scrollTo({ top: transcriptRef.current.scrollHeight, behavior: 'smooth' });
  }, [messages, loading]);

  useEffect(() => {
    const id = customerId.trim();
    if (!id) {
      setOrder(null);
      setOrderError('');
      setHistory([]);
      setHistoryLoading(false);
      setHistoryError('');
      setHistoryOpen(false);
      return undefined;
    }

    const controller = new AbortController();
    let active = true;
    async function refreshCurrentOrder() {
      try {
        const response = await fetch(`/api/orders/current?customerId=${encodeURIComponent(id)}`, { signal: controller.signal });
        const data = await response.json();
        if (!response.ok) throw new Error(data?.error || 'The order summary could not be loaded.');
        if (active) {
          setOrder(data?.order ?? null);
          setOrderError('');
        }
      } catch (cause) {
        if (active && cause.name !== 'AbortError') {
          setOrderError(cause instanceof Error ? cause.message : 'The order summary could not be loaded.');
        }
      }
    }

    async function refreshHistory() {
      try {
        const response = await fetch(`/api/orders/history?customerId=${encodeURIComponent(id)}`, { signal: controller.signal });
        const data = await response.json();
        if (!response.ok) throw new Error(data?.error || 'Order history could not be loaded.');
        if (active) {
          setHistory(Array.isArray(data?.orders) ? data.orders : []);
          setHistoryError('');
        }
      } catch (cause) {
        if (active && cause.name !== 'AbortError') {
          setHistoryError(cause instanceof Error ? cause.message : 'Order history could not be loaded.');
        }
      }
    }

    refreshCurrentOrder();
    refreshHistory();
    const interval = order ? window.setInterval(refreshCurrentOrder, 5000) : undefined;
    return () => {
      active = false;
      controller.abort();
      if (interval) window.clearInterval(interval);
    };
  }, [customerId, order?.id]);

  function changeCustomerId(value) {
    setCustomerId(value);
    setMessages(initialMessage());
    setOrderStatus('');
    setOrder(null);
    setOrderError('');
    setHistory([]);
    setHistoryLoading(false);
    setHistoryError('');
    setHistoryOpen(false);
    setError('');
  }

  async function refreshOrder(id = customerId.trim()) {
    if (!id) return;
    try {
      const response = await fetch(`/api/orders/current?customerId=${encodeURIComponent(id)}`);
      const data = await response.json();
      if (!response.ok) throw new Error(data?.error || 'The order summary could not be loaded.');
      if (id === customerId.trim()) {
        setOrder(data?.order ?? null);
        setOrderError('');
      }
    } catch (cause) {
      if (id === customerId.trim()) {
        setOrderError(cause instanceof Error ? cause.message : 'The order summary could not be loaded.');
      }
    }
  }

  async function refreshHistory(id = customerId.trim()) {
    if (!id) return;
    setHistoryLoading(true);
    try {
      const response = await fetch(`/api/orders/history?customerId=${encodeURIComponent(id)}`);
      const data = await response.json();
      if (!response.ok) throw new Error(data?.error || 'Order history could not be loaded.');
      if (id === customerId.trim()) {
        setHistory(Array.isArray(data?.orders) ? data.orders : []);
        setHistoryError('');
      }
    } catch (cause) {
      if (id === customerId.trim()) {
        setHistoryError(cause instanceof Error ? cause.message : 'Order history could not be loaded.');
      }
    } finally {
      if (id === customerId.trim()) setHistoryLoading(false);
    }
  }

  async function sendMessage(message = draft) {
    const text = message.trim();
    const id = customerId.trim();
    if (!text || loading) return;
    if (!id) {
      setError('Add a customer name or ID before sending a message.');
      return;
    }

    setError('');
    setDraft('');
    setMessages((current) => [...current, { id: crypto.randomUUID(), role: 'user', text }]);
    setLoading(true);
    try {
      const response = await fetch('/api/chat', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ customerId: id, message: text }),
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data?.error || 'The chat could not complete your request. Please try again.');
      if (typeof data?.response !== 'string') throw new Error('The chat returned an unexpected response. Please try again.');
      setMessages((current) => [...current, { id: crypto.randomUUID(), role: 'assistant', text: data.response }]);
      setOrderStatus(typeof data.orderStatus === 'string' ? data.orderStatus : '');
      await Promise.all([refreshOrder(id), refreshHistory(id)]);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Something went wrong. Please try again.');
    } finally {
      setLoading(false);
      inputRef.current?.focus();
    }
  }

  function handleSubmit(event) {
    event.preventDefault();
    sendMessage();
  }

  function handleKeyDown(event) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      sendMessage();
    }
  }

  return (
    <main className="h-dvh min-h-[600px] overflow-x-hidden bg-cream text-ink">
      <div className="mx-auto flex h-full min-w-0 max-w-[1440px] flex-col overflow-x-hidden px-4 py-5 sm:px-8 lg:px-12">
        <header className="flex items-center justify-between gap-4 pb-6">
          <a className="group flex items-center gap-3 rounded-lg focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-moss" href="#main" aria-label="Olive and Ember home">
            <span className="flex h-11 w-11 items-center justify-center rounded-full bg-moss text-cream"><Icon name="leaf" className="h-6 w-6" /></span>
            <span><span className="block font-display text-xl leading-none sm:text-2xl">olive &amp; ember</span><span className="mt-1 block text-[10px] font-semibold uppercase tracking-[.2em] text-leaf">a neighborhood table</span></span>
          </a>
          <a className="hidden items-center gap-2 rounded-full border border-sand bg-paper px-4 py-2 text-sm font-medium text-ink transition hover:border-leaf hover:text-moss focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-moss sm:flex" href="/devui" title="Available in the Development environment">
            <span className="h-2 w-2 rounded-full bg-leaf" /> Agent studio <Icon name="arrow" className="h-3.5 w-3.5" />
          </a>
        </header>

        <section id="main" className="grid min-h-0 min-w-0 flex-1 grid-rows-[minmax(0,1fr)] overflow-hidden rounded-[28px] border border-[#e8e4d8] bg-paper shadow-card lg:grid-cols-[minmax(0,1fr)_minmax(250px,300px)]">
          <div className="flex min-h-0 min-w-0 flex-col overflow-hidden">
            <div className="flex items-center justify-between border-b border-[#eeeae0] px-5 py-4 sm:px-8 sm:py-5">
              <div className="flex items-center gap-3">
                <span className="flex h-10 w-10 items-center justify-center rounded-full bg-[#edf1e9] text-moss lg:hidden"><Icon name="leaf" className="h-5 w-5" /></span>
                <div className="min-w-0"><h1 className="truncate font-display text-xl sm:text-2xl">Your guest table</h1><p className="mt-0.5 flex items-center gap-1.5 text-xs text-[#778078]"><span className="h-1.5 w-1.5 rounded-full bg-[#77966a]" /> Ready when you are</p></div>
              </div>
              <div className="flex items-center gap-2">
                <button type="button" onClick={() => { setHistoryOpen(true); refreshHistory(); }} className="inline-flex items-center gap-1.5 rounded-full border border-[#e7e3d9] bg-white px-3 py-2 text-[11px] font-semibold text-moss transition hover:border-leaf hover:bg-[#f7faf4] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-moss sm:text-xs" aria-haspopup="dialog" aria-expanded={historyOpen}>
                  <Icon name="clock" className="h-3.5 w-3.5" /> History
                </button>
                <div className="hidden items-center gap-2 rounded-full bg-[#f5f3ed] px-3 py-2 text-[11px] font-medium text-[#67736a] sm:flex sm:text-xs"><Icon name="clock" className="h-3.5 w-3.5" /> Demo experience</div>
              </div>
            </div>

            <div className="flex flex-wrap items-center gap-x-3 gap-y-2 border-b border-[#f0ede5] bg-[#fcfbf8] px-5 py-3 sm:px-8">
              <label htmlFor="customer-id" className="text-xs font-semibold text-[#68746a]">Customer ID</label>
              <input id="customer-id" value={customerId} onChange={(event) => changeCustomerId(event.target.value)} className="w-36 rounded-md border border-[#e7e3d9] bg-white px-2.5 py-1.5 text-sm text-ink outline-none transition focus:border-leaf focus:ring-2 focus:ring-leaf/20 disabled:cursor-not-allowed disabled:bg-[#f5f3ed]" autoComplete="off" spellCheck="false" disabled={loading} />
              <span className="text-[11px] text-[#92988e]">Your preferences stay with this ID.</span>
            </div>

            <details className="border-b border-[#eeeae0] bg-[#fcfbf8] px-5 py-3 lg:hidden">
              <summary className="cursor-pointer list-none rounded-md text-sm font-semibold text-moss focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-moss">
                <span className="flex items-center justify-between gap-3"><span>Your order</span><span className="truncate text-xs font-medium text-[#788276]">{order ? `${order.id} · ${order.orderStatus}` : 'No saved order yet'}</span></span>
              </summary>
              <OrderPanel order={order} error={orderError} compact />
            </details>

            <div ref={transcriptRef} className="chat-scroll min-h-0 min-w-0 flex-1 overflow-x-hidden overflow-y-auto overscroll-contain px-5 py-6 sm:px-8 sm:py-8">
              <div className="mx-auto flex w-full min-w-0 max-w-3xl flex-col gap-5" role="log" aria-label="Conversation messages" aria-live="polite" aria-relevant="additions">
                <div className="mb-1 text-center"><span className="rounded-full bg-[#f4f1e8] px-3 py-1 text-[10px] font-semibold uppercase tracking-[.15em] text-[#899184]">Today · welcome in</span></div>
                {messages.map((message) => <Message key={message.id} message={message} />)}
                {loading && <div className="flex items-end gap-3" role="status" aria-label="Olive and Ember is thinking">
                  <AssistantAvatar /><div className="rounded-2xl rounded-bl-sm bg-[#f3f2ed] px-4 py-3"><span className="flex gap-1.5"><i className="typing-dot"/><i className="typing-dot delay-150"/><i className="typing-dot delay-300"/></span></div><span className="pb-1 text-xs text-[#8b9389]">Putting that together…</span>
                </div>}
                {messages.length === 1 && !loading && <div className="grid grid-cols-1 gap-2 pt-1 sm:grid-cols-2">{starters.map((item) => <button key={item.title} className="group flex min-h-[66px] items-center gap-3 rounded-xl border border-[#ebe7dc] bg-white px-4 py-3 text-left transition hover:border-[#a9b79f] hover:bg-[#fafbf8] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-moss" onClick={() => sendMessage(item.prompt)}>
                  <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-[#f1f3ed] font-display text-lg text-moss transition group-hover:bg-[#e5ebdf]">{item.icon}</span><span className="min-w-0"><span className="block text-sm font-semibold text-ink">{item.title}</span><span className="mt-0.5 block truncate text-[11px] text-[#879087]">{item.prompt}</span></span><span className="ml-auto text-[#a2aa9e] transition group-hover:translate-x-0.5 group-hover:text-moss">→</span>
                </button>)}</div>}
              </div>
            </div>

            <div className="border-t border-[#eeeae0] bg-[#fffefa] px-4 py-4 sm:px-8 sm:py-5">
              <div className="mx-auto max-w-3xl">
                {orderStatus && <details className="mb-3 rounded-lg bg-[#f1f5ee] px-3 py-2 text-xs leading-5 text-[#50694f]">
                  <summary className="cursor-pointer font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-moss">Order update</summary>
                  <p className="mt-1 break-words [overflow-wrap:anywhere]">{orderStatus}</p>
                </details>}
                {error && <div className="mb-3 flex min-w-0 items-start gap-2 rounded-lg border border-[#f0d6ca] bg-[#fff7f3] px-3 py-2.5 text-sm text-[#9b5039]" role="alert"><span aria-hidden="true">!</span><span className="min-w-0 break-words [overflow-wrap:anywhere]">{error}</span></div>}
                <form onSubmit={handleSubmit} className="rounded-2xl border border-[#e4e1d7] bg-white p-2 shadow-[0_3px_16px_rgba(36,58,46,.04)] transition focus-within:border-[#a8b69f] focus-within:ring-2 focus-within:ring-leaf/15">
                  <label htmlFor="message" className="sr-only">Message Olive and Ember</label>
                  <textarea id="message" ref={inputRef} rows="2" value={draft} onChange={(event) => setDraft(event.target.value)} onKeyDown={handleKeyDown} placeholder="Ask about the menu, start an order, or check your status…" className="max-h-36 min-h-[50px] w-full resize-y border-0 bg-transparent px-3 pt-2 text-sm leading-6 text-ink outline-none placeholder:text-[#a0a69d] focus:ring-0" disabled={loading} />
                  <div className="flex items-center justify-between gap-3 px-1 pb-0.5">
                    <div className="flex items-center gap-2 text-[10px] text-[#92998f] sm:text-[11px]"><span className="flex h-6 w-6 items-center justify-center rounded-full bg-[#f2f3ee] text-leaf"><Icon name="plus" className="h-3.5 w-3.5" /></span><span>Shift + Enter for a new line</span></div>
                    <button type="submit" disabled={loading || !draft.trim()} className="inline-flex h-10 items-center gap-2 rounded-xl bg-moss px-4 text-sm font-semibold text-white transition hover:bg-ink focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-moss disabled:cursor-not-allowed disabled:bg-[#aab5a8]">
                      <span>{loading ? 'Sending' : 'Send'}</span><Icon name="send" className="h-4 w-4" />
                    </button>
                  </div>
                </form>
                <p className="mt-3 text-center text-[10px] leading-4 text-[#92998f] sm:text-[11px]">Local demo only: orders, payment checks, and fulfillment are simulated. Never enter real payment details.</p>
              </div>
            </div>
          </div>

          <aside className="hidden min-h-0 flex-col border-l border-[#eeeae0] bg-[#fcfbf8] p-5 lg:flex xl:p-6" aria-label="Current order summary">
            <div className="mb-5 border-b border-[#eae7de] pb-4">
              <p className="text-[10px] font-semibold uppercase tracking-[.18em] text-leaf">Order tracker</p>
              <h2 className="mt-1 font-display text-2xl text-ink">Your order</h2>
            </div>
            <OrderPanel order={order} error={orderError} />
          </aside>
        </section>
        <footer className="flex items-center justify-between gap-4 px-1 pt-4 text-[10px] text-[#899187] sm:text-xs"><span>Olive &amp; Ember <span className="mx-1">·</span> Something good is cooking</span><a className="rounded underline decoration-[#bcc2b7] underline-offset-4 hover:text-moss focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-moss lg:hidden" href="/devui">Agent studio</a></footer>
      </div>
      <HistoryDrawer history={history} loading={historyLoading} error={historyError} open={historyOpen} onClose={() => setHistoryOpen(false)} />
    </main>
  );
}

function HistoryDrawer({ history, loading, error, open, onClose }) {
  useEffect(() => {
    if (!open) return undefined;
    function handleKeyDown(event) {
      if (event.key === 'Escape') onClose();
    }
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [open, onClose]);

  return <div className={`fixed inset-0 z-50 ${open ? '' : 'pointer-events-none'}`} aria-hidden={!open}>
    <button type="button" aria-label="Close order history" onClick={onClose} className={`absolute inset-0 h-full w-full bg-ink/20 transition-opacity duration-200 ${open ? 'opacity-100' : 'opacity-0'}`} tabIndex={open ? 0 : -1} />
    <aside role="dialog" aria-modal="true" aria-labelledby="order-history-title" className={`absolute right-0 top-0 flex h-full w-full max-w-md flex-col border-l border-[#e6e2d8] bg-paper p-5 shadow-[-12px_0_40px_rgba(33,59,50,.12)] transition-transform duration-300 sm:p-7 ${open ? 'translate-x-0' : 'translate-x-full'}`}>
      <div className="flex items-start justify-between gap-4 border-b border-[#eae7de] pb-5">
        <div>
          <p className="text-[10px] font-semibold uppercase tracking-[.18em] text-leaf">Past visits</p>
          <h2 id="order-history-title" className="mt-1 font-display text-2xl text-ink">Order history</h2>
          <p className="mt-1 text-xs leading-5 text-[#7b8578]">Delivered and cancelled orders, newest first.</p>
        </div>
        <button type="button" onClick={onClose} tabIndex={open ? 0 : -1} className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full border border-[#e7e3d9] text-[#68746a] transition hover:border-leaf hover:text-moss focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-moss" aria-label="Close order history">
          <Icon name="close" className="h-4 w-4" />
        </button>
      </div>
      <div className="chat-scroll min-h-0 flex-1 overflow-y-auto py-5">
        {loading && <p className="text-sm text-[#7a8379]" role="status">Loading order history…</p>}
        {!loading && error && <p className="rounded-lg border border-[#f0d6ca] bg-[#fff7f3] px-3 py-2.5 text-sm leading-5 text-[#9b5039]" role="alert">{error}</p>}
        {!loading && !error && history.length === 0 && <div className="rounded-xl bg-[#f5f6f1] p-4 text-sm leading-6 text-[#7a8379]">Completed or cancelled orders will appear here after they leave your current order panel.</div>}
        {!loading && !error && history.length > 0 && <div className="flex flex-col gap-3">
          {history.map((item) => <HistoryCard key={item.id} order={item} />)}
        </div>}
      </div>
    </aside>
  </div>;
}

function HistoryCard({ order }) {
  return <article className="rounded-xl border border-[#e8e4da] bg-white p-4 shadow-[0_3px_14px_rgba(36,58,46,.03)]">
    <div className="flex items-start justify-between gap-3">
      <div>
        <p className="text-[10px] font-semibold uppercase tracking-[.14em] text-[#748071]">Order {order.id}</p>
        <p className="mt-1 text-[11px] text-[#7b8578]">{new Date(order.createdAtUtc).toLocaleString()}</p>
      </div>
      <span className="rounded-full bg-[#eef1e9] px-2.5 py-1 text-[10px] font-semibold capitalize text-moss">{order.orderStatus}</span>
    </div>
    <p className="mt-4 whitespace-pre-wrap break-words text-sm leading-6 text-[#34483d]">{order.summary || 'No item details were saved.'}</p>
    <div className="mt-4 grid grid-cols-2 gap-3 border-t border-[#eeeae0] pt-3 text-xs">
      <div><p className="text-[10px] font-semibold uppercase tracking-[.1em] text-[#81897f]">Payment</p><p className="mt-1 font-medium capitalize text-[#34483d]">{order.paymentMethod}</p><p className="mt-0.5 leading-5 text-[#7b8578]">{order.paymentStatus}</p></div>
      <div><p className="text-[10px] font-semibold uppercase tracking-[.1em] text-[#81897f]">Delivery</p><p className="mt-1 font-medium capitalize text-[#34483d]">{order.deliveryStatus}</p></div>
    </div>
  </article>;
}

function OrderPanel({ order, error, compact = false }) {
  if (!order) {
    return <div className={`text-sm leading-6 text-[#7a8379] ${compact ? 'pt-4' : ''}`}>
      {error ? <p role="status" className="text-[#9b5039]">{error}</p> : <p>Your saved order and agreed choices will appear here when checkout creates a draft.</p>}
      <p className="mt-3 text-xs leading-5 text-[#969b91]">Only details saved with this local demo customer ID are shown.</p>
    </div>;
  }

  return <div className="min-h-0 flex-1 overflow-y-auto pr-1">
    {error && <p role="status" className="mb-3 text-xs leading-5 text-[#9b5039]">Could not refresh: {error}</p>}
    <div className="rounded-xl bg-[#eef1e9] p-4">
      <p className="text-[10px] font-semibold uppercase tracking-[.14em] text-[#748071]">Order {order.id}</p>
      <p className="mt-2 text-sm font-semibold capitalize leading-5 text-moss">{order.orderStatus}</p>
      <p className="mt-1 text-[11px] text-[#7b8578]">Saved {new Date(order.createdAtUtc).toLocaleString()}</p>
    </div>

    <section className="mt-5">
      <h3 className="text-xs font-semibold uppercase tracking-[.12em] text-[#81897f]">Agreed order</h3>
      <p className="mt-2 whitespace-pre-wrap break-words text-sm leading-6 text-[#34483d]">{order.summary || 'No item details were saved.'}</p>
    </section>

    <section className="mt-5 border-t border-[#eae7de] pt-4">
      <h3 className="text-xs font-semibold uppercase tracking-[.12em] text-[#81897f]">Payment choice</h3>
      <p className="mt-2 text-sm font-medium capitalize text-[#34483d]">{order.paymentMethod}</p>
      <p className="mt-1 text-xs leading-5 text-[#7b8578]">{order.paymentStatus}</p>
      <p className="mt-2 text-[10px] leading-4 text-[#969b91]">Demo only. No payment was collected.</p>
    </section>

    <section className="mt-5 border-t border-[#eae7de] pt-4">
      <h3 className="text-xs font-semibold uppercase tracking-[.12em] text-[#81897f]">Simulated delivery</h3>
      <p className="mt-2 text-sm font-medium capitalize text-[#34483d]">{order.deliveryStatus}</p>
      <p className="mt-1 text-xs leading-5 text-[#7b8578]">Status refreshes while this chat is open.</p>
    </section>
  </div>;
}

function AssistantAvatar() {
  return <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-moss text-[#f9f7ef]"><Icon name="leaf" className="h-4 w-4" /></span>;
}

function Message({ message }) {
  const isUser = message.role === 'user';
  return <div className={`flex min-w-0 max-w-full items-end gap-3 ${isUser ? 'justify-end' : ''}`}>
    {!isUser && <AssistantAvatar />}
    <div className={`min-w-0 max-w-[88%] break-words rounded-2xl px-4 py-3 text-sm leading-6 [overflow-wrap:anywhere] sm:max-w-[78%] ${isUser ? 'rounded-br-sm bg-moss text-white' : 'rounded-bl-sm bg-[#f3f2ed] text-[#34483d]'}`}>
      {message.text.split('\n').map((line, index) => <p key={`${message.id}-${index}`} className={index ? 'mt-2' : ''}>{line || '\u00a0'}</p>)}
    </div>
    {isUser && <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-[#e8ece4] text-[11px] font-semibold text-moss" aria-label="You">You</span>}
  </div>;
}
