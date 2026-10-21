const ROOT = '/api/travel-plans';

async function readResponse(response) {
  const payload = await response.json().catch(() => null);
  if (response.ok) return payload;
  if (response.status === 404) {
    const error = new Error('This assessment is no longer available. Start a new one.');
    error.expired = true;
    throw error;
  }
  const fieldErrors = payload?.errors && Object.values(payload.errors).flat().join(' ');
  throw new Error(fieldErrors || payload?.detail || payload?.title || `Request failed (${response.status}).`);
}

export async function createTravelPlan(request, signal) {
  const payload = await readResponse(await fetch(ROOT, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request), signal,
  }));
  if (!payload?.planId || !payload?.statusUrl || !payload?.eventsUrl) throw new Error('The server returned an incomplete assessment response.');
  return payload;
}

export async function getTravelPlan(statusUrl, signal) {
  return readResponse(await fetch(statusUrl, { signal }));
}

export async function cancelTravelPlan(statusUrl, signal) {
  const response = await fetch(statusUrl, { method: 'DELETE', signal });
  if (response.status === 409) return response.json();
  return readResponse(response);
}
