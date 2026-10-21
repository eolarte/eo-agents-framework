import { useState } from 'react';

const DAY_MS = 86400000;

function dayNumber(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
  const [year, month, day] = value.split('-').map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
    ? Math.floor(date.getTime() / DAY_MS) : null;
}

function validate(values) {
  const errors = {};
  const start = dayNumber(values.startDate);
  const end = dayNumber(values.endDate);
  if (start === null) errors.startDate = 'Choose a valid start date.';
  if (end === null) errors.endDate = 'Choose a valid end date.';
  if (start !== null && end !== null && (end < start || end - start >= 14)) {
    errors.endDate = 'Choose a trip from 1 to 14 days, including both dates.';
  }
  const interests = values.interests.split(',').map((value) => value.trim()).filter(Boolean);
  if (interests.length > 12) errors.interests = 'Enter at most 12 interests.';
  else if (interests.some((value) => value.length > 40)) errors.interests = 'Keep each interest to 40 characters or fewer.';
  if (!values.dailyStartTime) errors.dailyStartTime = 'Choose a daily start time.';
  if (!values.dailyEndTime || values.dailyEndTime <= values.dailyStartTime) errors.dailyEndTime = 'End time must be later than start time.';
  return { errors, interests };
}

const INITIAL = {
  startDate: '', endDate: '', interests: '', pace: 'moderate', transportMode: 'walking',
  budgetLevel: 'medium', avoidCrowds: false, dailyStartTime: '09:00', dailyEndTime: '19:00',
};

function ChoiceGroup({ legend, name, value, options, onChange }) {
  return <fieldset className="choice-group"><legend>{legend}</legend><div className="choice-options">
    {options.map(([option, label]) => <label key={option} className={value === option ? 'is-checked' : ''}>
      <input type="radio" name={name} value={option} checked={value === option} onChange={() => onChange(option)} />{label}
    </label>)}
  </div></fieldset>;
}

export default function PreferencesPage({ destination, initialRequest, onBack, onSubmit, submitting, submitError }) {
  const [values, setValues] = useState(() => initialRequest ? {
    ...INITIAL, startDate: initialRequest.startDate, endDate: initialRequest.endDate,
    interests: initialRequest.preferences.interests.join(', '), pace: initialRequest.preferences.pace,
    transportMode: initialRequest.preferences.transportMode, budgetLevel: initialRequest.preferences.budgetLevel,
    avoidCrowds: initialRequest.preferences.avoidCrowds,
    dailyStartTime: initialRequest.preferences.dailyStartTime.slice(0, 5),
    dailyEndTime: initialRequest.preferences.dailyEndTime.slice(0, 5),
  } : INITIAL);
  const [attempted, setAttempted] = useState(false);
  const [touched, setTouched] = useState({});
  const { errors, interests } = validate(values);
  const valid = Object.keys(errors).length === 0;
  const set = (field, value) => setValues((previous) => ({ ...previous, [field]: value }));
  const touch = (field) => setTouched((previous) => ({ ...previous, [field]: true }));
  const hasError = (field) => !!errors[field] && (attempted || touched[field]);

  async function submit(event) {
    event.preventDefault();
    setAttempted(true);
    if (!valid || submitting) return;
    await onSubmit({
      destinationId: destination.id, startDate: values.startDate, endDate: values.endDate,
      preferences: { interests, pace: values.pace, transportMode: values.transportMode,
        budgetLevel: values.budgetLevel, avoidCrowds: values.avoidCrowds,
        dailyStartTime: `${values.dailyStartTime}:00`, dailyEndTime: `${values.dailyEndTime}:00` },
      language: 'en',
    });
  }

  function fieldError(name) { return hasError(name) ? <p className="form-error" id={`${name}-error`}>{errors[name]}</p> : null; }

  return <main id="main" className="flow-main">
    <div className="flow-topline"><span>TRIP PREFERENCES</span><span>02 / 03</span></div>
    <div className="flow-content">
      <button className="text-action" type="button" onClick={onBack}>← Change destination</button>
      <p className="eyebrow"><span className="eyebrow-line" /> MAKE IT YOURS</p>
      <h1 id="page-title" tabIndex="-1">Tell us about<br /><em>your trip.</em></h1>
      <p className="flow-lede">We’ll research conditions and places around your selected destination.</p>
      <div className="destination-summary"><span>DESTINATION</span><strong>{destination.name}</strong><small>{[destination.region, destination.country].filter(Boolean).join(', ')}</small></div>
      <form onSubmit={submit} noValidate>
        <div className="form-grid">
          <div className="form-field"><label htmlFor="startDate">Start date</label><input id="startDate" type="date" required value={values.startDate} onChange={(e) => set('startDate', e.target.value)} onBlur={() => touch('startDate')} aria-invalid={hasError('startDate')} aria-describedby={hasError('startDate') ? 'startDate-error' : undefined} />{fieldError('startDate')}</div>
          <div className="form-field"><label htmlFor="endDate">End date</label><input id="endDate" type="date" required value={values.endDate} onChange={(e) => set('endDate', e.target.value)} onBlur={() => touch('endDate')} aria-invalid={hasError('endDate')} aria-describedby={hasError('endDate') ? 'endDate-error' : undefined} />{fieldError('endDate')}</div>
        </div>
        <p className="form-hint">Trips can be 1–14 days. Past and distant dates use historical weather estimates.</p>
        <div className="form-field"><label htmlFor="interests">Interests <span className="optional">optional</span></label><textarea id="interests" rows="2" value={values.interests} onChange={(e) => set('interests', e.target.value)} onBlur={() => touch('interests')} placeholder="Museums, food, architecture" aria-invalid={hasError('interests')} aria-describedby={`interests-help${hasError('interests') ? ' interests-error' : ''}`} /><p id="interests-help" className="form-hint">Separate with commas. Up to 12 interests, 40 characters each.</p>{fieldError('interests')}</div>
        <ChoiceGroup legend="Pace" name="pace" value={values.pace} onChange={(value) => set('pace', value)} options={[["relaxed", "Relaxed"], ["moderate", "Moderate"], ["intensive", "Intensive"]]} />
        <ChoiceGroup legend="Transport" name="transport" value={values.transportMode} onChange={(value) => set('transportMode', value)} options={[["walking", "Walking"], ["transit", "Transit"], ["driving", "Driving"]]} />
        <ChoiceGroup legend="Budget" name="budget" value={values.budgetLevel} onChange={(value) => set('budgetLevel', value)} options={[["low", "Low"], ["medium", "Medium"], ["high", "High"]]} />
        <label className="checkbox-field"><input type="checkbox" checked={values.avoidCrowds} onChange={(e) => set('avoidCrowds', e.target.checked)} /><span>Avoid crowds where possible</span></label>
        <div className="form-grid">
          <div className="form-field"><label htmlFor="dailyStartTime">Daily start time</label><input id="dailyStartTime" type="time" value={values.dailyStartTime} onChange={(e) => set('dailyStartTime', e.target.value)} onBlur={() => touch('dailyStartTime')} aria-invalid={hasError('dailyStartTime')} aria-describedby={hasError('dailyStartTime') ? 'dailyStartTime-error' : undefined} />{fieldError('dailyStartTime')}</div>
          <div className="form-field"><label htmlFor="dailyEndTime">Daily end time</label><input id="dailyEndTime" type="time" value={values.dailyEndTime} onChange={(e) => set('dailyEndTime', e.target.value)} onBlur={() => touch('dailyEndTime')} aria-invalid={hasError('dailyEndTime')} aria-describedby={hasError('dailyEndTime') ? 'dailyEndTime-error' : undefined} />{fieldError('dailyEndTime')}</div>
        </div>
        {attempted && !valid && <p className="form-summary" role="alert">Review the highlighted fields before starting.</p>}
        {submitError && <p className="form-summary" role="alert">{submitError}</p>}
        <button className="primary-action" type="submit" disabled={!valid || submitting}>{submitting ? 'Starting assessment…' : 'Assess my trip →'}</button>
      </form>
    </div>
  </main>;
}
