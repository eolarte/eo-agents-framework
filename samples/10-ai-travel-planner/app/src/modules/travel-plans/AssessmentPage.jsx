import { useEffect } from 'react';

const AGENTS = [
  ['weather', 'Weather'], ['attractions', 'Attractions'], ['seasonality', 'Seasonality'],
  ['knowledge', 'Destination knowledge'], ['advisor', 'Advisor'],
];

const TERMINAL = new Set(['completed', 'partial', 'failed', 'cancelled']);

function label(value) {
  return ({ queued: 'Queued', running: 'Running', pending: 'Waiting', completed: 'Complete',
    partial: 'Partial', failed: 'Failed', cancelled: 'Cancelled', not_found: 'Not found' })[value] || value || 'Waiting';
}

function Status({ value }) { return <span className={`state-pill state-${value || 'pending'}`}>{label(value)}</span>; }

function Warnings({ items }) {
  return items?.length ? <ul className="warning-list">{items.map((item, index) => <li key={`${item}-${index}`}>{item}</li>)}</ul> : null;
}

function safeLink(value) {
  try { const url = new URL(value); return ['https:', 'http:'].includes(url.protocol) ? url.href : null; } catch { return null; }
}

function Evidence({ research }) {
  if (!research) return <p className="unavailable">No evidence returned.</p>;
  return <div className="evidence-meta">
    <div><span>Status</span><Status value={research.status} /></div>
    <div><span>Confidence</span><strong>{research.confidence || 'Unknown'}</strong></div>
    {research.retrievedAt && <div><span>Retrieved</span><time dateTime={research.retrievedAt}>{new Date(research.retrievedAt).toLocaleString()}</time></div>}
    <div><span>Source IDs</span><div className="source-list">{research.sourceIds?.length ? research.sourceIds.map((id) => {
      const href = safeLink(id);
      return href ? <a key={id} href={href} target="_blank" rel="noreferrer">{id}</a> : <code key={id}>{id}</code>;
    }) : 'None available'}</div></div>
  </div>;
}

function Section({ title, research, children }) {
  return <section className="research-card"><h2>{title}</h2><Evidence research={research} />{children}<Warnings items={research?.warnings} /></section>;
}

function basisLabel(value) {
  return value === 0 || String(value).toLowerCase() === 'forecast' ? 'Forecast' :
    value === 1 || String(value).toLowerCase() === 'historical' ? 'Historical estimate' : 'Basis unknown';
}

function amount(value, unit, digits = 1) { return value == null ? 'Unavailable' : `${Number(value).toFixed(digits)} ${unit}`; }

function Weather({ research }) {
  const data = research?.data;
  return <Section title="Weather" research={research}>
    {data?.periods?.length ? <div className="period-grid">{data.periods.map((period, index) => <div className="period-card" key={`${period.startDate}-${index}`}>
      <strong>{basisLabel(period.basis)}</strong><span>{period.startDate} to {period.endDate}</span>
      <p>Average high: {amount(period.averageMaxTemperatureC, data.temperatureUnit || '°C')}</p>
      <p>Average low: {amount(period.averageMinTemperatureC, data.temperatureUnit || '°C')}</p>
      <p>Average daily precipitation: {amount(period.averageDailyPrecipitationMm, data.precipitationUnit || 'mm')}</p>
      <small>{period.coverageDays ?? 0} of {period.totalDays ?? 0} days covered{period.sampleYears ? ` · ${period.sampleYears} sample years` : ''}</small>
    </div>)}</div> : <p className="unavailable">Weather periods are unavailable.</p>}
    {data?.days?.length > 0 && <details className="daily-details"><summary>Daily weather details</summary><div className="table-scroll"><table><thead><tr><th>Date</th><th>Basis</th><th>High</th><th>Low</th><th>Precipitation</th><th>Coverage</th></tr></thead><tbody>{data.days.map((day) => <tr key={day.date}><td>{day.date}</td><td>{basisLabel(day.basis)}</td><td>{amount(day.temperatureMaxC, data.temperatureUnit || '°C')}</td><td>{amount(day.temperatureMinC, data.temperatureUnit || '°C')}</td><td>{amount(day.precipitationMm, data.precipitationUnit || 'mm')}</td><td>{day.coverageSamples ?? 0}{day.sampleYears ? ` / ${day.sampleYears}` : ''}</td></tr>)}</tbody></table></div></details>}
    <p className="evidence-note">Historical values are averages from past years, not a prediction for your dates.</p>
    <Warnings items={data?.warnings} />
  </Section>;
}

function Seasonality({ research }) {
  const data = research?.data;
  return <Section title="Seasonality context" research={research}>
    {data ? <div className="fact-grid">
      <div><span>Level</span><strong>{data.level || 'Unknown'}</strong></div>
      <div><span>Geography</span><strong>{data.geographyLevel && data.geographyCode ? `${data.geographyLevel} ${data.geographyCode}` : 'Unavailable'}</strong></div>
      <div><span>Observation year</span><strong>{data.observationYear ?? 'Unavailable'}</strong></div>
      <div><span>Monthly index</span><strong>{data.monthlyIndex == null ? 'Unavailable' : Number(data.monthlyIndex).toFixed(2)}</strong></div>
      <div><span>Requested months</span><strong>{data.requestedMonths?.join(', ') || 'Unavailable'}</strong></div>
    </div> : <p className="unavailable">Regional seasonality evidence is unavailable.</p>}
    <p className="evidence-note">Eurostat regional accommodation nights provide context. They are not a city crowd forecast.</p>
    {data?.sourceUrl && safeLink(data.sourceUrl) && <p><a href={data.sourceUrl} target="_blank" rel="noreferrer">View Eurostat evidence ↗</a></p>}
    <Warnings items={data?.warnings} />
  </Section>;
}

function Attractions({ research }) {
  return <Section title="Nearby attractions" research={research}>
    {research?.data?.length > 0 && <p className="provider-credit">Place results provided by <span className="google-maps-attribution" translate="no">Google Maps</span></p>}
    {research?.data?.length ? <ol className="attraction-list">{research.data.map((place) => <li key={place.id}>
      <strong>{place.name}</strong><span>{place.categories?.join(' · ') || 'Category unavailable'}</span>
      <small>Coordinates: {place.latitude}, {place.longitude}</small>
      <small>Type and distance score: {typeof place.score === 'number' ? place.score.toFixed(2) : 'Unavailable'} · Source: {place.source?.provider || 'Google Places'} {place.source?.providerId || ''}</small>
    </li>)}</ol> : <p className="unavailable">{research?.status === 'failed' ? 'Attraction search failed. See the provider warning below.' : 'No matching nearby attractions were returned.'}</p>}
    {research?.data?.length > 0 && <p className="evidence-note">Places must match a visitor type or a requested interest type. The score then uses distance. It does not indicate popularity, opening hours, travel time, or visit duration.</p>}
  </Section>;
}

function Knowledge({ research }) {
  return <Section title="Destination knowledge" research={research}>
    {research?.data ? <p>{research.data}</p> : <p className="unavailable">No matching destination guide was found.</p>}
  </Section>;
}

function Results({ snapshot, onRestart }) {
  const result = snapshot.result;
  if (!result) return <div className="result-empty"><p>{snapshot.error || 'No assessment result was returned.'}</p><button type="button" className="primary-action" onClick={onRestart}>Try again</button></div>;
  return <>
    <div className="result-heading"><div><p className="eyebrow"><span className="eyebrow-line" /> YOUR ASSESSMENT</p><h1 id="page-title" tabIndex="-1">{result.destinationName}</h1><p>{result.startDate} to {result.endDate}</p></div><Status value={snapshot.status} /></div>
    <button type="button" className="text-action" onClick={onRestart}>← Start another assessment</button>
    <section className="advisor-card"><h2>Advisor perspective</h2>
      {result.advisor ? <><p>{result.advisor.summary}</p>{result.advisor.recommendations?.length ? <ol>{result.advisor.recommendations.map((recommendation, index) => <li key={`${recommendation.title}-${index}`}><h3>{recommendation.title}</h3><p>{recommendation.description}</p><small>Source IDs: {recommendation.sourceIds?.join(', ') || 'None cited'}</small></li>)}</ol> : <p className="unavailable">No recommendations were returned.</p>}</> : <p className="unavailable">Advisor recommendations are unavailable.</p>}
    </section>
    <Warnings items={result.warnings} />
    <Weather research={result.weather} />
    <Seasonality research={result.seasonality} />
    <Attractions research={result.attractions} />
    <Knowledge research={result.knowledge} />
  </>;
}

export default function AssessmentPage({ destination, dates, snapshot, error, expired, cancelling, onCancel, onRestart }) {
  const terminal = TERMINAL.has(snapshot?.status);
  const resultReady = terminal && !!snapshot?.result;
  useEffect(() => {
    if (resultReady) document.getElementById('page-title')?.focus();
  }, [resultReady]);
  return <main id="main" className="flow-main assessment-main">
    <div className="flow-topline"><span>TRIP ASSESSMENT</span><span>03 / 03</span></div>
    <div className="flow-content">
      {resultReady ? <Results snapshot={snapshot} onRestart={onRestart} /> : <>
        <p className="eyebrow"><span className="eyebrow-line" /> {destination.name.toUpperCase()}</p>
        <h1 id="page-title" tabIndex="-1">{expired ? 'Assessment expired.' : snapshot?.status === 'cancelled' ? 'Assessment cancelled.' : snapshot?.status === 'failed' ? 'Assessment unavailable.' : 'Research in progress.'}</h1>
        <p className="flow-lede">{dates.startDate} to {dates.endDate}</p>
        <p className="progress-message" role="status" aria-live="polite">{expired ? 'The server no longer has this job.' : snapshot?.status === 'cancelled' ? 'The assessment was cancelled.' : snapshot?.status === 'failed' ? snapshot.error || 'The assessment could not be completed.' : `Overall status: ${label(snapshot?.status)}.`}</p>
        <p className="sr-only" role="status" aria-live="polite" aria-atomic="true">{snapshot?.agent ? `${AGENTS.find(([key]) => key === snapshot.agent)?.[1] || snapshot.agent}: ${label(snapshot.agentStatus)}.` : ''}</p>
        {!expired && <ol className="agent-list">{AGENTS.map(([key, name]) => <li key={key}><span>{name}</span><Status value={snapshot?.agentStatuses?.[key]} /></li>)}</ol>}
        {error && <p className="form-summary" role="alert">{error}</p>}
        {terminal || expired ? <button className="primary-action" type="button" onClick={onRestart}>Try again</button> : <button className="secondary-action" type="button" onClick={onCancel} disabled={cancelling}>{cancelling ? 'Cancelling…' : 'Cancel assessment'}</button>}
      </>}
    </div>
  </main>;
}
