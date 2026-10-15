export async function searchDestinations(query, signal) {
  const response = await fetch(`/api/destinations/search?q=${encodeURIComponent(query)}`, { signal });
  const payload = await response.json();
  if (!response.ok || !Array.isArray(payload?.destinations)) {
    throw new Error('Destination search is unavailable.');
  }
  return payload.destinations;
}
