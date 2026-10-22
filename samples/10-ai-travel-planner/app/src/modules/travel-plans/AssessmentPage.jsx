import { useEffect } from 'react';
import { ArrowLeft, ArrowUpRight, ChevronDown, CircleCheck, CircleX, MapPin } from 'lucide-react';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from '@/components/ui/empty';
import { Spinner } from '@/components/ui/spinner';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';

const AGENTS = [
  ['weather', 'Weather'], ['attractions', 'Attractions'], ['seasonality', 'Seasonality'],
  ['knowledge', 'Destination knowledge'], ['advisor', 'Advisor'],
];

const TERMINAL = new Set(['completed', 'partial', 'failed', 'cancelled']);

function label(value) {
  return ({ queued: 'Queued', running: 'Running', pending: 'Waiting', completed: 'Complete',
    partial: 'Partial', failed: 'Failed', cancelled: 'Cancelled', not_found: 'Not found' })[value] || value || 'Waiting';
}

function Status({ value }) {
  const variant = value === 'failed' || value === 'cancelled' ? 'destructive' : value === 'completed' ? 'default' : 'secondary';
  return <Badge variant={variant}>{label(value)}</Badge>;
}

function Warnings({ items }) {
  return items?.length ? <Alert variant="default"><AlertTitle>Things to know</AlertTitle><AlertDescription><ul className="list-disc pl-5">{items.map((item, index) => <li key={`${item}-${index}`}>{item}</li>)}</ul></AlertDescription></Alert> : null;
}

function safeLink(value) {
  try { const url = new URL(value); return ['https:', 'http:'].includes(url.protocol) ? url.href : null; } catch { return null; }
}

function Evidence({ research }) {
  if (!research) return <p className="text-sm text-muted-foreground">No evidence returned.</p>;
  return <dl className="grid gap-3 text-sm sm:grid-cols-2">
    <div className="flex items-center gap-2"><dt className="text-muted-foreground">Status</dt><dd><Status value={research.status} /></dd></div>
    <div><dt className="text-muted-foreground">Confidence</dt><dd className="font-medium">{research.confidence || 'Unknown'}</dd></div>
    {research.retrievedAt && <div><dt className="text-muted-foreground">Retrieved</dt><dd><time dateTime={research.retrievedAt}>{new Date(research.retrievedAt).toLocaleString()}</time></dd></div>}
    <div className="sm:col-span-2"><dt className="text-muted-foreground">Source IDs</dt><dd className="flex flex-wrap gap-x-3 gap-y-1 break-all">{research.sourceIds?.length ? research.sourceIds.map((id) => {
      const href = safeLink(id);
      return href ? <a className="underline underline-offset-4" key={id} href={href} target="_blank" rel="noreferrer">{id}<span className="sr-only"> (opens in new tab)</span></a> : <code key={id}>{id}</code>;
    }) : 'None available'}</dd></div>
  </dl>;
}

function Section({ title, research, children }) {
  return <section><Card className="h-full">
    <CardHeader><CardTitle>{title}</CardTitle><CardDescription>Research evidence and source details</CardDescription></CardHeader>
    <CardContent className="flex flex-col gap-5"><Evidence research={research} />{children}<Warnings items={research?.warnings} /></CardContent>
  </Card></section>;
}

function basisLabel(value) {
  return value === 0 || String(value).toLowerCase() === 'forecast' ? 'Forecast' :
    value === 1 || String(value).toLowerCase() === 'historical' ? 'Historical estimate' : 'Basis unknown';
}

function amount(value, unit, digits = 1) { return value == null ? 'Unavailable' : `${Number(value).toFixed(digits)} ${unit}`; }

function Weather({ research }) {
  const data = research?.data;
  return <Section title="Weather" research={research}>
    {data?.periods?.length ? <div className="grid gap-3 md:grid-cols-2">{data.periods.map((period, index) => <Card key={`${period.startDate}-${index}`}>
      <CardHeader><CardTitle className="text-base">{basisLabel(period.basis)}</CardTitle><CardDescription>{period.startDate} to {period.endDate}</CardDescription></CardHeader>
      <CardContent className="flex flex-col gap-2 text-sm">
        <p>Average high: {amount(period.averageMaxTemperatureC, data.temperatureUnit || '°C')}</p>
        <p>Average low: {amount(period.averageMinTemperatureC, data.temperatureUnit || '°C')}</p>
        <p>Average daily precipitation: {amount(period.averageDailyPrecipitationMm, data.precipitationUnit || 'mm')}</p>
        <p className="text-muted-foreground">{period.coverageDays ?? 0} of {period.totalDays ?? 0} days covered{period.sampleYears ? ` · ${period.sampleYears} sample years` : ''}</p>
      </CardContent>
    </Card>)}</div> : <p className="text-sm text-muted-foreground">Weather periods are unavailable.</p>}
    {data?.days?.length > 0 && <Collapsible className="group rounded-lg border p-3"><CollapsibleTrigger asChild><Button variant="ghost" className="w-full justify-between">Daily weather details<ChevronDown data-icon="inline-end" className="transition-transform group-data-[state=open]:rotate-180" /></Button></CollapsibleTrigger><CollapsibleContent><div className="overflow-x-auto pt-3"><Table><TableHeader><TableRow><TableHead>Date</TableHead><TableHead>Basis</TableHead><TableHead>High</TableHead><TableHead>Low</TableHead><TableHead>Precipitation</TableHead><TableHead>Coverage</TableHead></TableRow></TableHeader><TableBody>{data.days.map((day) => <TableRow key={day.date}><TableCell>{day.date}</TableCell><TableCell>{basisLabel(day.basis)}</TableCell><TableCell>{amount(day.temperatureMaxC, data.temperatureUnit || '°C')}</TableCell><TableCell>{amount(day.temperatureMinC, data.temperatureUnit || '°C')}</TableCell><TableCell>{amount(day.precipitationMm, data.precipitationUnit || 'mm')}</TableCell><TableCell>{day.coverageSamples ?? 0}{day.sampleYears ? ` / ${day.sampleYears}` : ''}</TableCell></TableRow>)}</TableBody></Table></div></CollapsibleContent></Collapsible>}
    <p className="text-sm text-muted-foreground">Historical values are averages from past years, not a prediction for your dates.</p>
    <Warnings items={data?.warnings} />
  </Section>;
}

function Seasonality({ research }) {
  const data = research?.data;
  const facts = data ? [
    ['Level', data.level || 'Unknown'],
    ['Geography', data.geographyLevel && data.geographyCode ? `${data.geographyLevel} ${data.geographyCode}` : 'Unavailable'],
    ['Observation year', data.observationYear ?? 'Unavailable'],
    ['Monthly index', data.monthlyIndex == null ? 'Unavailable' : Number(data.monthlyIndex).toFixed(2)],
    ['Requested months', data.requestedMonths?.join(', ') || 'Unavailable'],
  ] : [];
  return <Section title="Seasonality context" research={research}>
    {data ? <dl className="grid gap-4 sm:grid-cols-2">{facts.map(([name, value]) => <div key={name}><dt className="text-sm text-muted-foreground">{name}</dt><dd className="font-medium">{value}</dd></div>)}</dl> : <p className="text-sm text-muted-foreground">Regional seasonality evidence is unavailable.</p>}
    <p className="text-sm text-muted-foreground">Eurostat regional accommodation nights provide context. They are not a city crowd forecast.</p>
    {data?.sourceUrl && safeLink(data.sourceUrl) && <Button asChild variant="link" className="self-start px-0"><a href={data.sourceUrl} target="_blank" rel="noreferrer">View Eurostat evidence<ArrowUpRight data-icon="inline-end" /><span className="sr-only"> (opens in new tab)</span></a></Button>}
    <Warnings items={data?.warnings} />
  </Section>;
}

function Attractions({ research }) {
  return <Section title="Nearby attractions" research={research}>
    {research?.data?.length > 0 && <p className="text-sm text-muted-foreground">Place results provided by <span className="font-medium text-foreground" translate="no">Google Maps</span></p>}
    {research?.data?.length ? <ol className="grid gap-3 sm:grid-cols-2">{research.data.map((place) => <li key={place.id}><Card className="h-full"><CardHeader><CardTitle className="text-base">{place.name}</CardTitle><CardDescription>{place.categories?.join(' · ') || 'Category unavailable'}</CardDescription></CardHeader><CardContent className="flex flex-col gap-1 text-sm text-muted-foreground"><p>Coordinates: {place.latitude}, {place.longitude}</p><p>Type and distance score: {typeof place.score === 'number' ? place.score.toFixed(2) : 'Unavailable'}</p><p>Source: {place.source?.provider || 'Google Places'} {place.source?.providerId || ''}</p></CardContent></Card></li>)}</ol> : <p className="text-sm text-muted-foreground">{research?.status === 'failed' ? 'Attraction search failed. See the provider warning below.' : 'No matching nearby attractions were returned.'}</p>}
    {research?.data?.length > 0 && <p className="text-sm text-muted-foreground">Places must match a visitor type or a requested interest type. The score then uses distance. It does not indicate popularity, opening hours, travel time, or visit duration.</p>}
  </Section>;
}

function Knowledge({ research }) {
  return <Section title="Destination knowledge" research={research}>
    {research?.data ? <p className="whitespace-pre-wrap text-sm leading-relaxed">{research.data}</p> : <p className="text-sm text-muted-foreground">No matching destination guide was found.</p>}
  </Section>;
}

function Results({ snapshot, onRestart }) {
  const result = snapshot.result;
  if (!result) return <Empty><EmptyHeader><EmptyTitle>No assessment result</EmptyTitle><EmptyDescription>{snapshot.error || 'No assessment result was returned.'}</EmptyDescription></EmptyHeader><Button type="button" onClick={onRestart}>Try again</Button></Empty>;
  return <div className="flex flex-col gap-6">
    <div className="flex flex-wrap items-start justify-between gap-4"><div className="flex flex-col gap-2"><p className="text-sm text-muted-foreground">Your assessment</p><h1 id="page-title" tabIndex="-1" className="text-3xl font-semibold tracking-tight sm:text-4xl">{result.destinationName}</h1><p className="text-muted-foreground">{result.startDate} to {result.endDate}</p></div><Status value={snapshot.status} /></div>
    <Button type="button" variant="ghost" className="self-start" onClick={onRestart}><ArrowLeft data-icon="inline-start" />Start another assessment</Button>
    <Card><CardHeader><CardTitle>Advisor perspective</CardTitle><CardDescription>Recommendations based on the available research</CardDescription></CardHeader><CardContent className="flex flex-col gap-5">
      {result.advisor ? <><p className="leading-relaxed">{result.advisor.summary}</p>{result.advisor.recommendations?.length ? <ol className="flex flex-col gap-4">{result.advisor.recommendations.map((recommendation, index) => <li key={`${recommendation.title}-${index}`} className="rounded-lg border p-4"><h3 className="font-medium">{recommendation.title}</h3><p className="mt-2 text-sm">{recommendation.description}</p><p className="mt-2 break-all text-xs text-muted-foreground">Source IDs: {recommendation.sourceIds?.join(', ') || 'None cited'}</p></li>)}</ol> : <p className="text-sm text-muted-foreground">No recommendations were returned.</p>}</> : <p className="text-sm text-muted-foreground">Advisor recommendations are unavailable.</p>}
    </CardContent></Card>
    <Warnings items={result.warnings} />
    <div className="grid gap-6 lg:grid-cols-2"><Weather research={result.weather} /><Seasonality research={result.seasonality} /><Attractions research={result.attractions} /><Knowledge research={result.knowledge} /></div>
  </div>;
}

export default function AssessmentPage({ destination, dates, snapshot, error, expired, cancelling, onCancel, onRestart }) {
  const terminal = TERMINAL.has(snapshot?.status);
  const resultReady = terminal && !!snapshot?.result;
  useEffect(() => {
    if (resultReady) document.getElementById('page-title')?.focus();
  }, [resultReady]);
  return <main id="main" className="mx-auto w-full max-w-6xl px-4 py-8 sm:px-6 sm:py-12">
    <div className="mb-8 flex items-center justify-between text-sm text-muted-foreground"><span>Trip assessment</span><span>Step 3 of 3</span></div>
    {resultReady ? <Results snapshot={snapshot} onRestart={onRestart} /> : <div className="mx-auto flex max-w-3xl flex-col gap-6">
      <div className="flex flex-col gap-3"><p className="flex items-center gap-2 text-sm text-muted-foreground"><MapPin className="size-4" aria-hidden="true" />{destination.name}</p><h1 id="page-title" tabIndex="-1" className="text-3xl font-semibold tracking-tight sm:text-4xl">{expired ? 'Assessment expired.' : snapshot?.status === 'cancelled' ? 'Assessment cancelled.' : snapshot?.status === 'failed' ? 'Assessment unavailable.' : 'Research in progress.'}</h1><p className="text-muted-foreground">{dates.startDate} to {dates.endDate}</p></div>
      <Alert><AlertTitle>{expired ? 'Job expired' : snapshot?.status === 'cancelled' ? 'Cancelled' : snapshot?.status === 'failed' ? 'Assessment failed' : 'Working on your assessment'}</AlertTitle><AlertDescription role="status" aria-live="polite">{expired ? 'The server no longer has this job.' : snapshot?.status === 'cancelled' ? 'The assessment was cancelled.' : snapshot?.status === 'failed' ? snapshot.error || 'The assessment could not be completed.' : `Overall status: ${label(snapshot?.status)}.`}</AlertDescription></Alert>
      <p className="sr-only" role="status" aria-live="polite" aria-atomic="true">{snapshot?.agent ? `${AGENTS.find(([key]) => key === snapshot.agent)?.[1] || snapshot.agent}: ${label(snapshot.agentStatus)}.` : ''}</p>
      {!expired && <Card><CardHeader><CardTitle>Research steps</CardTitle><CardDescription>Each source reports its own progress.</CardDescription></CardHeader><CardContent><ol className="flex flex-col gap-3">{AGENTS.map(([key, name]) => { const state = snapshot?.agentStatuses?.[key]; return <li key={key} className="flex items-center justify-between gap-4 rounded-lg border px-4 py-3"><span className="flex items-center gap-3">{state === 'running' ? <Spinner className="size-4" aria-hidden="true" /> : state === 'completed' ? <CircleCheck className="size-4 text-primary" aria-hidden="true" /> : state === 'failed' ? <CircleX className="size-4 text-destructive" aria-hidden="true" /> : <span className="size-4" aria-hidden="true" />}{name}</span><Status value={state} /></li>; })}</ol></CardContent></Card>}
      {error && <Alert variant="destructive" role="alert"><AlertTitle>Assessment error</AlertTitle><AlertDescription>{error}</AlertDescription></Alert>}
      {terminal || expired ? <Button className="self-start" type="button" onClick={onRestart}>Try again</Button> : <Button className="self-start" variant="outline" type="button" onClick={onCancel} disabled={cancelling}>{cancelling ? <><Spinner data-icon="inline-start" />Cancelling…</> : 'Cancel assessment'}</Button>}
    </div>}
  </main>;
}
