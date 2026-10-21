import { useEffect, useState } from 'react';
import DestinationPage from './modules/destinations/DestinationPage.jsx';
import PreferencesPage from './modules/travel-plans/PreferencesPage.jsx';
import AssessmentPage from './modules/travel-plans/AssessmentPage.jsx';
import { useTravelPlan } from './modules/travel-plans/useTravelPlan.js';
import './modules/travel-plans/travelPlans.css';

function BrandIcon() {
  return (
    <svg width="25" height="25" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <circle cx="12" cy="12" r="9" />
      <path d="m15.5 8.5-2.3 4.7-4.7 2.3 2.3-4.7 4.7-2.3Z" />
    </svg>
  );
}

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
    <div className="app-shell">
      <header className="site-header">
        <a href="#main" className="brand" aria-label="Wayfinder, skip to main content">
          <span className="brand-mark"><BrandIcon /></span>
          <span>wayfinder<span className="brand-period">.</span></span>
        </a>
        <span className="header-note">A starting point for everywhere</span>
      </header>
      {step === 'destination' && <DestinationPage onContinue={(selected) => { setDestination(selected); setStep('preferences'); }} />}
      {step === 'preferences' && <PreferencesPage destination={destination} initialRequest={lastRequest} onBack={changeDestination} onSubmit={submit} submitting={plan.creating} submitError={plan.error} />}
      {step === 'assessment' && <AssessmentPage destination={destination} dates={dates} snapshot={plan.snapshot} error={plan.error} expired={plan.expired} cancelling={plan.cancelling} onCancel={plan.cancel} onRestart={restart} />}
    </div>
  );
}
