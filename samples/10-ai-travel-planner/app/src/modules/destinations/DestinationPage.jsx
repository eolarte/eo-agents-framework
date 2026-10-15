import { useRef } from 'react';
import { useDestinationSearch } from './useDestinationSearch.js';
import { coordinate, placeDetails } from './destinationFormat.js';

function Icon({ name, size = 20 }) {
  const paths = {
    search: <><circle cx="10.8" cy="10.8" r="6.8" /><path d="m16 16 4.2 4.2" /></>,
    arrow: <><path d="M4 12h16m-6-6 6 6-6 6" /></>,
    pin: <><path d="M19 10c0 5-7 11-7 11S5 15 5 10a7 7 0 1 1 14 0Z" /><circle cx="12" cy="10" r="2.2" /></>,
    globe: <><circle cx="12" cy="12" r="9" /><path d="M3 12h18M12 3c2.6 2.5 4 5.5 4 9s-1.4 6.5-4 9c-2.6-2.5-4-5.5-4-9s1.4-6.5 4-9Z" /></>,
    close: <><path d="M5 5 19 19M19 5 5 19" /></>,
    check: <><path d="m4 12 5 5L20 6" /></>,
  };
  return <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>;
}

export default function DestinationPage() {
  const inputRef = useRef(null);
  const {
    query, selected, search, activeIndex, showResults, status,
    setOpen, setActiveIndex, changeQuery, selectDestination,
    clearSelection, handleKeyDown, retrySearch,
  } = useDestinationSearch();

  function clearAndFocus() {
    clearSelection();
    inputRef.current?.focus();
  }

  return (
    <main id="main" className="main-layout">
        <section className="intro" aria-labelledby="page-title">
          <div className="intro-copy">
            <p className="eyebrow"><span className="eyebrow-line" /> YOUR NEXT CHAPTER</p>
            <h1 id="page-title">It starts with<br /><em>a place.</em></h1>
            <p className="intro-description">Find the destination you have in mind. We’ll help you pick the right place when names look alike.</p>
          </div>
          <div className="route-art" aria-hidden="true">
            <div className="art-grid" />
            <span className="art-circle art-circle-one" /><span className="art-circle art-circle-two" />
            <svg className="art-route" viewBox="0 0 480 280" preserveAspectRatio="none"><path d="M32 225 C125 215 122 100 210 124 S316 214 448 42" /></svg>
            <span className="art-point art-point-one"><span /></span>
            <span className="art-point art-point-two"><span /></span>
            <span className="art-point art-point-three"><span /></span>
            <span className="art-label art-label-one">BEGIN HERE</span>
            <span className="art-label art-label-two">GO ANYWHERE</span>
            <span className="art-cross">✳</span>
          </div>
          <p className="intro-footnote">01 <span /> CHOOSE YOUR DESTINATION</p>
        </section>

        <section className="search-panel" aria-labelledby="search-title">
          <div className="panel-topline"><span>DESTINATION SEARCH</span><span>01 / 01</span></div>
          <div className="panel-content">
            <div className="panel-heading"><span className="heading-icon"><Icon name="globe" size={24} /></span><p>First, find your place</p></div>
            <h2 id="search-title">Where would you<br />like to go?</h2>
            <p className="search-description">Search by city or place name, then choose the location that matches your trip.</p>

            <div className="field-group">
              <label htmlFor="destination-search">DESTINATION</label>
              <div className={`search-field ${selected ? 'is-selected' : ''}`}>
                <Icon name="search" size={21} />
                <input
                  ref={inputRef}
                  id="destination-search"
                  type="text"
                  value={query}
                  onChange={(event) => changeQuery(event.target.value)}
                  onFocus={() => setOpen(true)}
                  onBlur={() => setOpen(false)}
                  onKeyDown={handleKeyDown}
                  placeholder="Try Paris or Springfield"
                  autoComplete="off"
                  maxLength={100}
                  role="combobox"
                  aria-autocomplete="list"
                  aria-expanded={showResults}
                  aria-controls={showResults ? 'destination-results' : undefined}
                  aria-activedescendant={showResults && activeIndex >= 0 ? `destination-option-${activeIndex}` : undefined}
                  aria-describedby="search-help search-status"
                />
                {selected && <button className="clear-button" type="button" onMouseDown={(event) => event.preventDefault()} onClick={clearAndFocus} aria-label="Clear selected destination"><Icon name="close" size={17} /></button>}
              </div>
              <p id="search-help" className="field-help">Use a city or town name. Add more letters to narrow the results.</p>

              {showResults && (
                <div className="results-list" id="destination-results" role="listbox" aria-label="Matching destinations">
                  <div className="results-heading">MATCHING PLACES <span>{search.destinations.length} FOUND</span></div>
                  {search.destinations.map((destination, index) => (
                    <div
                      id={`destination-option-${index}`}
                      key={destination.id}
                      className={`result-option ${activeIndex === index ? 'is-active' : ''}`}
                      role="option"
                      aria-selected={activeIndex === index}
                      onMouseDown={(event) => event.preventDefault()}
                      onClick={() => selectDestination(destination)}
                      onMouseEnter={() => setActiveIndex(index)}
                    >
                      <span className="result-pin"><Icon name="pin" size={18} /></span>
                      <span className="result-text"><strong>{destination.name}</strong><small>{placeDetails(destination)}</small></span>
                      <Icon name="arrow" size={17} />
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div id="search-status" className={`search-status status-${search.kind}`} role="status" aria-live="polite">
              {search.kind === 'loading' && !selected && <span className="status-spinner" aria-hidden="true" />}
              {search.kind === 'error' && !selected && <span className="status-symbol" aria-hidden="true">!</span>}
              {status}
              {search.kind === 'error' && !selected && <button type="button" className="retry-button" onClick={retrySearch}>Try again</button>}
            </div>

            {selected ? (
              <div className="selection-card" aria-label="Selected destination">
                <div className="selection-title"><span className="selection-check"><Icon name="check" size={16} /></span><span>DESTINATION SELECTED</span></div>
                <h3>{selected.name}</h3>
                <p className="selection-place">{placeDetails(selected)}</p>
                <dl className="selection-details">
                  <div><dt>LOCATION ID</dt><dd>{selected.id}</dd></div>
                  <div><dt>COORDINATES</dt><dd>{coordinate(selected.latitude, 'N', 'S')} · {coordinate(selected.longitude, 'E', 'W')}</dd></div>
                  <div><dt>TIME ZONE</dt><dd>{selected.timezone}</dd></div>
                </dl>
              </div>
            ) : (
              <div className="tip-card"><span className="tip-icon">✳</span><p><strong>A little detail helps.</strong> Places often share a name. Check the region and country before you choose.</p></div>
            )}
          </div>
          <div className="panel-footer"><span><span className="footer-dot" /> Ready when you are</span><span>DESTINATION DATA VIA <a href="https://open-meteo.com/en/docs/geocoding-api" target="_blank" rel="noreferrer">OPEN-METEO</a>, BASED ON <a href="https://www.geonames.org/" target="_blank" rel="noreferrer">GEONAMES</a></span></div>
        </section>
    </main>
  );
}
