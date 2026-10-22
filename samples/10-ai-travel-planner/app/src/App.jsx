import { useEffect, useState } from 'react';
import { Compass } from 'lucide-react';
import DestinationPage from './modules/destinations/DestinationPage.jsx';
import PreferencesPage from './modules/travel-plans/PreferencesPage.jsx';
import AssessmentPage from './modules/travel-plans/AssessmentPage.jsx';
import { useTravelPlan } from './modules/travel-plans/useTravelPlan.js';

const STEPS = [
  { id: 'destination', label: 'Destination' },
  { id: 'preferences', label: 'Preferences' },
  { id: 'assessment', label: 'Assessment' },
];

export default function App() {
  const [step, setStep] = useState('destination');
  const [destination, setDestination] = useState(null);
  const [dates, setDates] = useState(null);
  const [lastRequest, setLastRequest] = useState(null);
  const plan = useTravelPlan();

  useEffect(() => {
    if (step !== 'destination') document.getElementById('page-title')?.focus();
  }, [step]);

  function changeDestination() {
    plan.reset();
    setDestination(null);
    setLastRequest(null);
    setStep('destination');
  }

  async function submit(request) {
    setLastRequest(request);
    const started = await plan.create(request);
    if (started) {
      setDates({ startDate: request.startDate, endDate: request.endDate });
      setStep('assessment');
    }
  }

  function restart() {
    plan.reset();
    setStep('preferences');
  }

  return (
    <div className="min-h-screen bg-background text-foreground">
      <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-md focus:bg-background focus:px-4 focus:py-2 focus:shadow-md">
        Skip to main content
      </a>
      <header className="mx-auto flex w-full max-w-5xl items-center justify-between gap-4 px-4 py-6 sm:px-6 lg:px-8">
        <div className="flex items-center gap-3" aria-label="Wayfinder">
          <span className="flex size-9 items-center justify-center rounded-lg bg-primary text-primary-foreground"><Compass className="size-5" aria-hidden="true" /></span>
          <span className="text-lg font-semibold tracking-tight">Wayfinder</span>
        </div>
        <span className="hidden text-sm text-muted-foreground sm:block">Plan with a clearer picture</span>
      </header>
      <nav className="mx-auto w-full max-w-5xl px-4 pb-6 sm:px-6 lg:px-8" aria-label="Trip planning progress">
        <ol className="grid grid-cols-3 gap-2 border-b pb-4 sm:gap-6">
          {STEPS.map((item, index) => (
            <li key={item.id} aria-current={step === item.id ? 'step' : undefined} className="group flex min-w-0 items-center gap-2 text-sm text-muted-foreground aria-[current=step]:font-medium aria-[current=step]:text-foreground">
              <span className="flex size-6 shrink-0 items-center justify-center rounded-full border text-xs group-aria-[current=step]:border-primary group-aria-[current=step]:bg-primary group-aria-[current=step]:text-primary-foreground" aria-hidden="true">{index + 1}</span>
              <span className="truncate">{item.label}</span>
            </li>
          ))}
        </ol>
      </nav>
      {step === 'destination' && <DestinationPage onContinue={(selected) => { setDestination(selected); setStep('preferences'); }} />}
      {step === 'preferences' && <PreferencesPage destination={destination} initialRequest={lastRequest} onBack={changeDestination} onSubmit={submit} submitting={plan.creating} submitError={plan.error} />}
      {step === 'assessment' && <AssessmentPage destination={destination} dates={dates} snapshot={plan.snapshot} error={plan.error} expired={plan.expired} cancelling={plan.cancelling} onCancel={plan.cancel} onRestart={restart} />}
    </div>
  );
}
