# AI travel planner

This sample begins the AI travel planner with a live destination search and a
destination picker. It uses Open-Meteo's Geocoding API to find locations and
keeps the selected destination's provider ID, coordinates, and timezone in the
browser state.

This first milestone does not include weather, places, agents, retrieval,
itinerary generation, or trip preference submission.

## Architecture

The sample runs as one ASP.NET Core application. Backend feature code lives in
`app/Modules/`; each module owns its API, application contract, domain types,
infrastructure, service registration, and route mapping. `app/Program.cs` composes
the modules and the shared static-file host.

The React app follows the same feature boundary under
`app/src/modules/`. The destinations module owns its page, search state,
provider API call, and destination formatting. `app/src/App.jsx` composes the page
with the app shell. New capabilities should join the host as modules instead
of spreading feature code across it.

## Run the sample

Build the React frontend first. The generated site is written to
`app/wwwroot/customer-ui/` and served by ASP.NET Core.

```bash
cd samples/10-ai-travel-planner/app
npm ci
npm run build
cd ../../..
dotnet run --project samples/10-ai-travel-planner/app/ai-travel-planner.csproj --urls http://localhost:5000
```

Open http://localhost:5000 to search for a destination. For frontend-only
iteration, run `npm run dev` in the `app/` directory while the backend is
running on port 5000; Vite proxies `/api` requests to ASP.NET Core. Rebuild the
frontend before running the production app to see the latest UI.

Run the focused backend tests from the repository root with:

```bash
dotnet test samples/10-ai-travel-planner/test/ai-travel-planner.Tests.csproj
```

The backend requires no API key or provider configuration. It sends search
terms of at least two characters to the unauthenticated Open-Meteo Geocoding
API and returns up to ten matching results. Provider failures are returned as
an API error so the UI can invite the user to retry.

Destination data is provided through [Open-Meteo's Geocoding API](https://open-meteo.com/en/docs/geocoding-api)
and is based on [GeoNames](https://www.geonames.org/). Keep this attribution
visible when reusing or extending the sample.
