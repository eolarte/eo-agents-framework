import { useState } from 'react';
import { ArrowLeft, ArrowRight, MapPin } from 'lucide-react';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Checkbox } from '@/components/ui/checkbox';
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel, FieldLegend, FieldSet } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Spinner } from '@/components/ui/spinner';
import { Textarea } from '@/components/ui/textarea';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';

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
  return <FieldSet>
    <FieldLegend variant="label" id={`${name}-legend`}>{legend}</FieldLegend>
    <FieldGroup><ToggleGroup type="single" value={value} onValueChange={(next) => { if (next) onChange(next); }} aria-labelledby={`${name}-legend`} variant="outline" spacing={2} className="flex flex-wrap justify-start">
      {options.map(([option, label]) => <ToggleGroupItem key={option} value={option} aria-label={label}>{label}</ToggleGroupItem>)}
    </ToggleGroup></FieldGroup>
  </FieldSet>;
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

  function fieldError(name) { return hasError(name) ? <FieldError id={`${name}-error`}>{errors[name]}</FieldError> : null; }

  return <main id="main" className="mx-auto w-full max-w-5xl px-4 py-8 sm:px-6 sm:py-12">
    <div className="mb-8 flex items-center justify-between text-sm text-muted-foreground"><span>Trip preferences</span><span>Step 2 of 3</span></div>
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-3">
        <Button variant="ghost" size="sm" type="button" onClick={onBack} className="self-start"><ArrowLeft data-icon="inline-start" />Change destination</Button>
        <h1 id="page-title" tabIndex="-1" className="text-3xl font-semibold tracking-tight sm:text-4xl">Tell us about your trip</h1>
        <p className="text-muted-foreground">We’ll research conditions and places around your selected destination.</p>
      </div>
      <Card>
        <CardHeader><CardDescription>Destination</CardDescription><CardTitle className="flex items-center gap-2"><MapPin className="size-5" aria-hidden="true" />{destination.name}</CardTitle></CardHeader>
        {[destination.region, destination.country].filter(Boolean).length > 0 && <CardContent className="text-sm text-muted-foreground">{[destination.region, destination.country].filter(Boolean).join(', ')}</CardContent>}
      </Card>
      <Card>
        <CardHeader><CardTitle>Plan your days</CardTitle><CardDescription>Trips can be 1–14 days. Past and distant dates use historical weather estimates.</CardDescription></CardHeader>
        <CardContent>
          <form onSubmit={submit} noValidate className="flex flex-col gap-8">
            <FieldGroup>
              <div className="grid gap-5 sm:grid-cols-2">
                <Field data-invalid={hasError('startDate') || undefined}>
                  <FieldLabel htmlFor="startDate">Start date</FieldLabel>
                  <Input id="startDate" type="date" required value={values.startDate} onChange={(e) => set('startDate', e.target.value)} onBlur={() => touch('startDate')} aria-invalid={hasError('startDate')} aria-describedby={hasError('startDate') ? 'startDate-error' : undefined} />
                  {fieldError('startDate')}
                </Field>
                <Field data-invalid={hasError('endDate') || undefined}>
                  <FieldLabel htmlFor="endDate">End date</FieldLabel>
                  <Input id="endDate" type="date" required value={values.endDate} onChange={(e) => set('endDate', e.target.value)} onBlur={() => touch('endDate')} aria-invalid={hasError('endDate')} aria-describedby={hasError('endDate') ? 'endDate-error' : undefined} />
                  {fieldError('endDate')}
                </Field>
              </div>
              <Field data-invalid={hasError('interests') || undefined}>
                <FieldLabel htmlFor="interests">Interests <span className="font-normal text-muted-foreground">(optional)</span></FieldLabel>
                <Textarea id="interests" rows={3} value={values.interests} onChange={(e) => set('interests', e.target.value)} onBlur={() => touch('interests')} placeholder="Museums, food, architecture" aria-invalid={hasError('interests')} aria-describedby={`interests-help${hasError('interests') ? ' interests-error' : ''}`} />
                <FieldDescription id="interests-help">Separate with commas. Up to 12 interests, 40 characters each.</FieldDescription>
                {fieldError('interests')}
              </Field>
            </FieldGroup>
            <FieldGroup>
              <ChoiceGroup legend="Pace" name="pace" value={values.pace} onChange={(value) => set('pace', value)} options={[["relaxed", "Relaxed"], ["moderate", "Moderate"], ["intensive", "Intensive"]]} />
              <ChoiceGroup legend="Transport" name="transport" value={values.transportMode} onChange={(value) => set('transportMode', value)} options={[["walking", "Walking"], ["transit", "Transit"], ["driving", "Driving"]]} />
              <ChoiceGroup legend="Budget" name="budget" value={values.budgetLevel} onChange={(value) => set('budgetLevel', value)} options={[["low", "Low"], ["medium", "Medium"], ["high", "High"]]} />
              <FieldSet>
                <FieldLegend variant="label">Crowds</FieldLegend>
                <FieldGroup><Field orientation="horizontal"><Checkbox id="avoidCrowds" checked={values.avoidCrowds} onCheckedChange={(checked) => set('avoidCrowds', checked === true)} /><FieldLabel htmlFor="avoidCrowds" className="font-normal">Avoid crowds where possible</FieldLabel></Field></FieldGroup>
              </FieldSet>
              <div className="grid gap-5 sm:grid-cols-2">
                <Field data-invalid={hasError('dailyStartTime') || undefined}>
                  <FieldLabel htmlFor="dailyStartTime">Daily start time</FieldLabel>
                  <Input id="dailyStartTime" type="time" value={values.dailyStartTime} onChange={(e) => set('dailyStartTime', e.target.value)} onBlur={() => touch('dailyStartTime')} aria-invalid={hasError('dailyStartTime')} aria-describedby={hasError('dailyStartTime') ? 'dailyStartTime-error' : undefined} />
                  {fieldError('dailyStartTime')}
                </Field>
                <Field data-invalid={hasError('dailyEndTime') || undefined}>
                  <FieldLabel htmlFor="dailyEndTime">Daily end time</FieldLabel>
                  <Input id="dailyEndTime" type="time" value={values.dailyEndTime} onChange={(e) => set('dailyEndTime', e.target.value)} onBlur={() => touch('dailyEndTime')} aria-invalid={hasError('dailyEndTime')} aria-describedby={hasError('dailyEndTime') ? 'dailyEndTime-error' : undefined} />
                  {fieldError('dailyEndTime')}
                </Field>
              </div>
            </FieldGroup>
            {attempted && !valid && <Alert variant="destructive" role="alert"><AlertTitle>Review your trip details</AlertTitle><AlertDescription>Review the highlighted fields before starting.</AlertDescription></Alert>}
            {submitError && <Alert variant="destructive" role="alert"><AlertTitle>Assessment could not start</AlertTitle><AlertDescription>{submitError}</AlertDescription></Alert>}
            <Button type="submit" disabled={submitting} className="self-start">{submitting ? <><Spinner data-icon="inline-start" />Starting assessment…</> : <>Assess my trip<ArrowRight data-icon="inline-end" /></>}</Button>
          </form>
        </CardContent>
      </Card>
    </div>
  </main>;
}
