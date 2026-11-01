import { useEffect, useRef, useState } from 'react';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Spinner } from '@/components/ui/spinner';

let mapsLoadPromise;
let mapsAuthFailed = false;
const authFailureListeners = new Set();

function loadGoogleMaps(apiKey) {
  if (mapsAuthFailed) return Promise.reject(new Error('Google Maps authentication failed.'));
  if (window.google?.maps?.importLibrary) return Promise.resolve(window.google.maps);
  if (mapsLoadPromise) return mapsLoadPromise;

  mapsLoadPromise = new Promise((resolve, reject) => {
    const callbackName = '__travelPlannerMapsReady';
    window.gm_authFailure = () => {
      mapsAuthFailed = true;
      authFailureListeners.forEach((listener) => listener());
    };
    const script = document.createElement('script');
    const url = new URL('https://maps.googleapis.com/maps/api/js');
    url.searchParams.set('key', apiKey);
    url.searchParams.set('loading', 'async');
    url.searchParams.set('libraries', 'maps,marker');
    url.searchParams.set('callback', callbackName);

    const fail = () => {
      delete window[callbackName];
      script.remove();
      mapsLoadPromise = undefined;
      reject(new Error('Google Maps could not load.'));
    };

    window[callbackName] = () => {
      delete window[callbackName];
      resolve(window.google.maps);
    };
    script.onerror = fail;
    script.async = true;
    script.src = url.toString();
    document.head.append(script);
  });

  return mapsLoadPromise;
}

function validCoordinates(place) {
  const lat = Number(place.latitude);
  const lng = Number(place.longitude);
  return Number.isFinite(lat) && Number.isFinite(lng) && lat >= -90 && lat <= 90 && lng >= -180 && lng <= 180;
}

export default function AttractionMap({ places }) {
  const mapElement = useRef(null);
  const openPlace = useRef(null);
  const [status, setStatus] = useState('loading');
  const [error, setError] = useState('');
  const [activeId, setActiveId] = useState(null);
  const mappedPlaces = places.filter(validCoordinates);
  const apiKey = import.meta.env.GOOGLE_PLACES_API_KEY;
  const mapId = import.meta.env.VITE_GOOGLE_MAPS_MAP_ID || 'DEMO_MAP_ID';

  useEffect(() => {
    if (!mappedPlaces.length || !apiKey) {
      setError(apiKey ? '' : 'Set GOOGLE_PLACES_API_KEY before building the frontend to enable the map.');
      setStatus('unavailable');
      return;
    }

    let cancelled = false;
    let markers = [];
    let infoWindow;
    const onAuthFailure = () => {
      setError('Google Maps rejected the API key. Check Maps JavaScript API access, billing, and key restrictions.');
      setStatus('unavailable');
    };
    authFailureListeners.add(onAuthFailure);
    setError('');
    setStatus('loading');
    setActiveId(null);

    async function createMap() {
      try {
        const maps = await loadGoogleMaps(apiKey);
        const [{ Map }, { AdvancedMarkerElement }] = await Promise.all([
          maps.importLibrary('maps'), maps.importLibrary('marker'),
        ]);
        if (cancelled || !mapElement.current) return;

        const first = mappedPlaces[0];
        const map = new Map(mapElement.current, {
          center: { lat: Number(first.latitude), lng: Number(first.longitude) },
          zoom: 13,
          mapId,
          mapTypeControl: false,
          streetViewControl: false,
        });
        const bounds = new maps.LatLngBounds();
        infoWindow = new maps.InfoWindow();
        const markerById = new globalThis.Map();

        markers = mappedPlaces.map((place) => {
          const position = { lat: Number(place.latitude), lng: Number(place.longitude) };
          bounds.extend(position);
          const marker = new AdvancedMarkerElement({ map, position, title: place.name });
          markerById.set(place.id, marker);
          marker.addListener('click', () => showPlace(place));
          return marker;
        });

        function showPlace(place) {
          const marker = markerById.get(place.id);
          const content = document.createElement('div');
          const name = document.createElement('strong');
          name.textContent = place.name;
          content.append(name);
          if (place.categories?.length) {
            const categories = document.createElement('p');
            categories.textContent = place.categories.join(' · ');
            content.append(categories);
          }
          infoWindow.setContent(content);
          infoWindow.open({ anchor: marker, map });
          map.panTo(marker.position);
          setActiveId(place.id);
        }

        openPlace.current = showPlace;
        if (mappedPlaces.length > 1) map.fitBounds(bounds, 48);
        if (mapsAuthFailed) throw new Error('Google Maps authentication failed.');
        setStatus('ready');
      } catch {
        if (!cancelled) {
          setError(mapsAuthFailed
            ? 'Google Maps rejected the API key. Check Maps JavaScript API access, billing, and key restrictions.'
            : 'Google Maps could not load. Check the browser connection and Maps JavaScript API configuration.');
          setStatus('unavailable');
        }
      }
    }

    createMap();
    return () => {
      cancelled = true;
      authFailureListeners.delete(onAuthFailure);
      openPlace.current = null;
      infoWindow?.close();
      markers.forEach((marker) => { marker.map = null; });
    };
  }, [places, apiKey, mapId]);

  if (!mappedPlaces.length) return <p className="text-sm text-muted-foreground">No attractions with map coordinates were returned.</p>;

  return <div className="flex flex-col gap-3">
    {status === 'loading' && <p className="flex items-center gap-2 text-sm text-muted-foreground" role="status"><Spinner className="size-4" aria-hidden="true" />Loading map…</p>}
    {status === 'unavailable' && <Alert role="alert"><AlertTitle>Map unavailable</AlertTitle><AlertDescription>{error} You can still see all attractions in the list view.</AlertDescription></Alert>}
    <div ref={mapElement} role="region" aria-label="Map of nearby attractions" className={status === 'unavailable' ? 'hidden' : 'h-80 w-full rounded-lg border bg-muted sm:h-96'} />
    {status === 'ready' && <div className="flex flex-col gap-2"><p className="text-sm text-muted-foreground">Select a place to locate it on the map.</p><ol className="grid gap-1 sm:grid-cols-2 lg:grid-cols-3">{mappedPlaces.map((place) => <li key={place.id}><Button type="button" variant={activeId === place.id ? 'secondary' : 'ghost'} className="h-auto w-full justify-start whitespace-normal text-left" aria-pressed={activeId === place.id} onClick={() => openPlace.current?.(place)}>{place.name}</Button></li>)}</ol></div>}
  </div>;
}
