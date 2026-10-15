export function placeDetails(destination) {
  return [destination.region, destination.country].filter(Boolean).join(', ');
}

export function coordinate(value, positive, negative) {
  return `${Math.abs(value).toFixed(4)}° ${value >= 0 ? positive : negative}`;
}
