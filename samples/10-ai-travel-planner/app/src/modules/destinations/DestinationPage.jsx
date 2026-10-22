import { useRef } from 'react';
import { ArrowRight, Check, Globe2, Info, MapPin, Search, X } from 'lucide-react';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card';
import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from '@/components/ui/empty';
import { Field, FieldDescription, FieldGroup, FieldLabel } from '@/components/ui/field';
import { InputGroup, InputGroupAddon, InputGroupButton, InputGroupInput } from '@/components/ui/input-group';
import { Spinner } from '@/components/ui/spinner';
import { cn } from '@/lib/utils';
import { useDestinationSearch } from './useDestinationSearch.js';
import { coordinate, placeDetails } from './destinationFormat.js';

export default function DestinationPage({ onContinue }) {
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

  const isLoading = search.kind === 'loading' && !selected;
  const isError = search.kind === 'error' && !selected;
  const isEmpty = search.kind === 'ready' && search.destinations.length === 0 && !selected;

  return (
    <main id="main" className="mx-auto grid w-full max-w-5xl gap-8 px-4 py-8 sm:px-6 sm:py-12 lg:grid-cols-[minmax(0,0.8fr)_minmax(0,1.2fr)] lg:gap-12 lg:px-8 lg:py-20">
      <section aria-labelledby="page-title" className="flex flex-col gap-4 lg:pt-8">
        <Badge variant="secondary" className="w-fit">Step 1 of 3 · Destination</Badge>
        <h1 id="page-title" className="text-3xl font-semibold tracking-tight sm:text-4xl">Where would you like to go?</h1>
        <p className="max-w-md text-muted-foreground">Find the destination you have in mind. Check the region and country when places share a name.</p>
      </section>

      <Card aria-labelledby="search-title" className="min-w-0">
        <CardHeader>
          <div className="mb-2 flex size-10 items-center justify-center rounded-md bg-muted text-muted-foreground" aria-hidden="true"><Globe2 className="size-5" /></div>
          <CardTitle id="search-title">Choose a destination</CardTitle>
          <CardDescription>Search by city or place name, then select the location that matches your trip.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-5">
          <FieldGroup>
            <Field>
              <FieldLabel htmlFor="destination-search">Destination</FieldLabel>
              <InputGroup>
                <InputGroupInput
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
                <InputGroupAddon align="inline-start"><Search aria-hidden="true" /></InputGroupAddon>
                {selected && (
                  <InputGroupAddon align="inline-end">
                    <InputGroupButton size="icon-xs" type="button" onMouseDown={(event) => event.preventDefault()} onClick={clearAndFocus} aria-label="Clear selected destination"><X aria-hidden="true" /></InputGroupButton>
                  </InputGroupAddon>
                )}
              </InputGroup>
              <FieldDescription id="search-help">Use a city or town name. Add more letters to narrow the results.</FieldDescription>
            </Field>
          </FieldGroup>

          {showResults && (
            <div className="max-h-72 overflow-y-auto rounded-md border bg-popover p-1 shadow-md">
              <p className="px-2 py-1.5 text-xs font-medium text-muted-foreground">Matching places · {search.destinations.length} found</p>
              <div id="destination-results" role="listbox" aria-label="Matching destinations">
                {search.destinations.map((destination, index) => (
                  <div
                    id={`destination-option-${index}`}
                    key={destination.id}
                    role="option"
                    aria-selected={activeIndex === index}
                    className={cn('flex cursor-pointer items-center gap-3 rounded-sm px-2 py-2 text-sm outline-none hover:bg-accent hover:text-accent-foreground', activeIndex === index && 'bg-accent text-accent-foreground')}
                    onMouseDown={(event) => event.preventDefault()}
                    onClick={() => selectDestination(destination)}
                    onMouseEnter={() => setActiveIndex(index)}
                  >
                    <MapPin className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
                    <span className="flex min-w-0 flex-1 flex-col"><span className="font-medium">{destination.name}</span><span className="truncate text-xs text-muted-foreground">{placeDetails(destination)}</span></span>
                    <ArrowRight className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
                  </div>
                ))}
              </div>
            </div>
          )}

          <div id="search-status" role="status" aria-live="polite" className="flex items-center gap-2 text-sm text-muted-foreground">
            {isLoading && <Spinner aria-hidden="true" />}
            <span>{status}</span>
          </div>

          {isError && (
            <Alert variant="destructive">
              <AlertTitle>Search unavailable</AlertTitle>
              <AlertDescription className="flex flex-wrap items-center gap-2">Please try again. <Button variant="outline" size="sm" type="button" onClick={retrySearch}>Try again</Button></AlertDescription>
            </Alert>
          )}

          {isEmpty && (
            <Empty className="border">
              <EmptyHeader>
                <EmptyTitle>No matching destinations</EmptyTitle>
                <EmptyDescription>Try another spelling or a nearby city.</EmptyDescription>
              </EmptyHeader>
            </Empty>
          )}

          {selected ? (
            <Card aria-label="Selected destination" className="bg-muted/40 shadow-none">
              <CardHeader>
                <Badge variant="secondary" className="w-fit"><Check aria-hidden="true" /> Destination selected</Badge>
                <CardTitle>{selected.name}</CardTitle>
                <CardDescription>{placeDetails(selected)}</CardDescription>
              </CardHeader>
              <CardContent>
                <dl className="grid gap-4 text-sm sm:grid-cols-2">
                  <div><dt className="text-muted-foreground">Location ID</dt><dd className="font-medium">{selected.id}</dd></div>
                  <div><dt className="text-muted-foreground">Coordinates</dt><dd className="font-medium">{coordinate(selected.latitude, 'N', 'S')} · {coordinate(selected.longitude, 'E', 'W')}</dd></div>
                  <div><dt className="text-muted-foreground">Time zone</dt><dd className="font-medium">{selected.timezone}</dd></div>
                </dl>
              </CardContent>
            </Card>
          ) : !isEmpty && !isError && (
            <Alert>
              <Info aria-hidden="true" />
              <AlertTitle>A little detail helps</AlertTitle>
              <AlertDescription>Places often share a name. Check the region and country before you choose.</AlertDescription>
            </Alert>
          )}
        </CardContent>
        <CardFooter className="flex flex-col items-start gap-4 border-t pt-6">
          {selected && <Button className="w-full sm:w-auto" type="button" onClick={() => onContinue(selected)}>Continue to trip preferences <ArrowRight data-icon="inline-end" aria-hidden="true" /></Button>}
          <p className="text-xs text-muted-foreground">Destination data via <a className="underline underline-offset-4 hover:text-foreground" href="https://open-meteo.com/en/docs/geocoding-api" target="_blank" rel="noreferrer">Open-Meteo</a>, based on <a className="underline underline-offset-4 hover:text-foreground" href="https://www.geonames.org/" target="_blank" rel="noreferrer">GeoNames</a>.</p>
        </CardFooter>
      </Card>
    </main>
  );
}
