import { useEffect, useState } from 'react';
import { searchDestinations } from './destinationApi.js';
import { placeDetails } from './destinationFormat.js';

const SEARCH_DELAY_MS = 300;

function searchStatus({ selected, trimmedQuery, search }) {
  if (selected) return `${selected.name}, ${placeDetails(selected)} selected.`;
  if (!trimmedQuery) return 'Enter a city or place to search.';
  if (trimmedQuery.length < 2) return 'Enter at least two characters.';
  if (search.kind === 'loading') return 'Searching destinations…';
  if (search.kind === 'error') return 'Destination search is unavailable. Please try again.';
  if (search.kind === 'ready' && search.destinations.length === 0) {
    return `No destinations found for “${trimmedQuery}”. Try a different spelling.`;
  }
  if (search.kind === 'ready') {
    return `${search.destinations.length} destinations found. Use arrow keys to explore.`;
  }
  return '';
}

export function useDestinationSearch() {
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState(null);
  const [search, setSearch] = useState({ kind: 'idle', destinations: [] });
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const [retry, setRetry] = useState(0);
  const trimmedQuery = query.trim();
  const canSearch = !selected && trimmedQuery.length >= 2;
  const showResults = open && canSearch && search.kind === 'ready' && search.destinations.length > 0;

  useEffect(() => {
    if (!canSearch) return undefined;

    const controller = new AbortController();
    const timer = window.setTimeout(async () => {
      try {
        const destinations = await searchDestinations(trimmedQuery, controller.signal);
        setSearch({ kind: 'ready', destinations });
        setActiveIndex(-1);
      } catch (error) {
        if (error.name !== 'AbortError') setSearch({ kind: 'error', destinations: [] });
      }
    }, SEARCH_DELAY_MS);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [canSearch, trimmedQuery, retry]);

  function changeQuery(value) {
    setQuery(value);
    setSelected(null);
    setActiveIndex(-1);
    setOpen(true);
    setSearch({ kind: value.trim().length >= 2 ? 'loading' : 'idle', destinations: [] });
  }

  function selectDestination(destination) {
    setSelected(destination);
    setQuery(destination.name);
    setSearch({ kind: 'idle', destinations: [] });
    setOpen(false);
    setActiveIndex(-1);
  }

  function clearSelection() {
    setQuery('');
    setSelected(null);
    setSearch({ kind: 'idle', destinations: [] });
    setOpen(false);
    setActiveIndex(-1);
  }

  function handleKeyDown(event) {
    if (event.key === 'Escape') {
      setOpen(false);
      setActiveIndex(-1);
      return;
    }
    if (!showResults) return;
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActiveIndex((current) => Math.min(current + 1, search.destinations.length - 1));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActiveIndex((current) => Math.max(current - 1, 0));
    } else if (event.key === 'Enter' && activeIndex >= 0) {
      event.preventDefault();
      selectDestination(search.destinations[activeIndex]);
    }
  }

  function retrySearch() {
    setSearch({ kind: 'loading', destinations: [] });
    setRetry((value) => value + 1);
  }

  return {
    query,
    selected,
    search,
    open,
    activeIndex,
    showResults,
    status: searchStatus({ selected, trimmedQuery, search }),
    setOpen,
    setActiveIndex,
    changeQuery,
    selectDestination,
    clearSelection,
    handleKeyDown,
    retrySearch,
  };
}
