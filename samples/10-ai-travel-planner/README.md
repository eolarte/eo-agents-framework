# AI travel planner

This sample lets a user select a destination, set trip preferences, and start
a travel assessment. The browser shows job progress and research results. The
server gets weather, attractions, and tourism data. An AI Advisor uses that
data to give recommendations. The sample does not make an itinerary.

## Architecture

The sample runs as one ASP.NET Core application. The host serves the React
site and the API. It keeps provider keys on the server.

```text
                         AI TRAVEL PLANNER: ONE APPLICATION

  +---------------------------- BROWSER ----------------------------+
  | React: destination -> preferences -> assessment                 |
  | Search and create: HTTP   Progress: SSE   Status: HTTP GET      |
  +-------------------------------+---------------------------------+
                                  |
                                  | /api/* and static files
                                  v
  +------------------------- ASP.NET CORE HOST ---------------------+
  | Program.cs: register modules, map routes, serve customer-ui/    |
  |                                                                 |
  |  +--------------+  +---------+  +-------------+  +---------+    |
  |  | Destinations |  | Weather |  | Attractions |  | Tourism |    |
  |  | search / ID  |  | data    |  | places      |  | region  |    |
  |  +------+-------+  +----+----+  +------+------+  +----+----+    |
  |         |               |              |              |         |
  |  +------v---------------v--------------v--------------v-----+   |
  |  | Travel Plans: API -> job store -> workers -> Advisor      |  |
  |  |               status GET / events SSE / cancel DELETE    |   |
  |  +-----------------------------+----------------------------+   |
  +--------------------------------+--------------------------------+
                                   |
                    server-side provider calls
                                   v
  +-----------------------------------------------------------------+
  | Open-Meteo | Google Places | GISCO | Eurostat | OpenAI API      |
  +-----------------------------------------------------------------+
```

Each backend module owns its API, application contracts, domain types, and
provider adapters. `app/Program.cs` connects the modules. React keeps each
feature in `app/src/modules/`. Vite writes the site to
`app/wwwroot/customer-ui/`. ASP.NET Core serves those files.

```text
  app/
  |-- Program.cs                 Host setup and module registration
  |-- Modules/
  |   |-- Destinations/          Search and ID lookup
  |   |-- Weather/               Forecast and past weather
  |   |-- Attractions/           Nearby places
  |   |-- Tourism/               Regional visitor data
  |   `-- TravelPlans/           Jobs, research, and Advisor
  |       |-- Api/               Routes and background workers
  |       |-- Application/       Job store and research workflow
  |       |-- Domain/            Request and result types
  |       `-- Infrastructure/    Research agents and OpenAI client
  |-- Shared/Routing/            Contract for a future routing provider
  |-- src/modules/               React feature code
  `-- wwwroot/customer-ui/      Built React site

  knowledge/                     Demo source files for the Knowledge index
```

### Assessment data flow

The server resolves the selected destination ID before it starts a job. It
does not accept coordinates in the job request. The job store keeps jobs in
memory. The queue has a capacity limit. Two background workers take jobs from
the queue.

```text
  Browser                    Travel Plans module
     |                              |
     | POST /api/travel-plans       |
     +----------------------------->| Validate request
     |                              | Resolve destination ID
     |<-----------------------------+ 202 + job ID and URLs
     |                              |
     | GET status / GET events      | Queue -> background worker
     |<-----------------------------+              |
     |       status / SSE                          v
     |                                  +-----------------------+
     |                                  | Four research branches|
     |                                  +-----------+-----------+
     |                                              |
     |          +------------------+----------------+------------------+
     |          |                  |                |                  |
     |       Weather          Attractions      Seasonality         Knowledge
     |     Open-Meteo        Google Places    GISCO/Eurostat      local guides
     |          |                  |                |              city match
     |          +------------------+----------------+------------------+
     |                                              |
     |                                              v
     |                                      Join evidence + warnings
     |                                              |
     |                                              v
     |                                       Advisor -> OpenAI API
     |                                              |
     |                                              v
     |                                     Save result in job store
     |<---------------------------------------------+ status / SSE
     |
     +-- DELETE /api/travel-plans/{id} -----------> Cancel job
```

The four research branches run at the same time. The Advisor runs after they
join. It can use only source IDs from the research results. A provider or model
error can give a partial result. The Knowledge branch reads the local demo
guides. It returns `not_found` when no guide matches the destination.

The browser uses server-sent events (SSE) for progress. It also gets job
status with HTTP when it must update or recover state. Jobs do not survive a
server restart. The server keeps completed, partial, failed, and cancelled
jobs for 30 minutes. Browser job state is lost when the page reloads.

The direct research routes also remain available:

```text
  GET /api/destinations/search -> Destinations -> Open-Meteo Geocoding
  GET /api/weather             -> Weather      -> Open-Meteo Weather
  GET /api/attractions         -> Attractions  -> Google Places
  GET /api/tourism             -> Tourism      -> GISCO -> Eurostat
```

## Run

Build the site before you run the server:

```bash
cd samples/10-ai-travel-planner/app
npm ci
npm run build
cd ../../..
dotnet run --project samples/10-ai-travel-planner/app/ai-travel-planner.csproj --urls http://localhost:5000
```

Open http://localhost:5000. Select a destination, set preferences, and start
an assessment. The UI shows five agent statuses. It then shows Advisor
recommendations and the available evidence.

For frontend work, run `npm run dev` in `app/`. Vite sends `/api` requests to
the server on port 5000. Build the site again to view frontend changes through
ASP.NET Core.

Set `GOOGLE_PLACES_API_KEY` in the server environment to get live attractions.
The key needs Places API (New) access and billing. If the key is absent, the
attractions route returns HTTP 503. If the provider fails, it returns HTTP 502.
Open-Meteo weather and geocoding do not need a key. See `app/.env.example` for
the variable names. Do not put keys in the repository.

Set `AZURE_OPENAI_API_KEY` and `AZURE_OPENAI_DEPLOYMENT_NAME` in the server
environment to start travel assessment jobs. The variable names are from an
older sample. This app uses `OpenAIClient` and the **OpenAI API**. It does not
use an Azure OpenAI endpoint. The keys stay on the server. The app uses Agent
Framework 1.23.0 and an Advisor prompt with a version number.

## Research endpoints

First, search `/api/destinations/search?q=Paris`. Enter at least two characters.
The route returns up to ten results. Select a result. Use its
`id`, `latitude`, `longitude`, and `timezone` in these requests:

```text
GET /api/weather?destinationId=open-meteo:2988507&latitude=48.8566&longitude=2.3522&timezone=Europe%2FParis&startDate=2027-07-01&endDate=2027-07-03
GET /api/attractions?destinationId=open-meteo:2988507&latitude=48.8566&longitude=2.3522&interests=art,history
GET /api/tourism?destinationId=open-meteo:2988507&startDate=2027-07-01&endDate=2027-07-03
```

### Weather

Ask for 1 to 14 days. For dates in the 16-day forecast range, the server uses
an Open-Meteo forecast. For other dates, it uses samples from the same calendar
day in the last ten complete years. A trip can have forecast and historical
periods. Historical values are averages. For February 29 in a year without a
leap day, the server uses adjacent days. The response gives the data basis,
coverage, units, warnings, and source. A provider error returns HTTP 502.

### Attractions

The server uses Google Places Nearby Search (New). It searches within 5 km
and returns at most 20 places. It requests only the place ID, display name,
location, types, and primary type. Known interests map to Places types. With
no known interest, the server searches visitor-related types. It also checks
the returned types before it ranks results. A generic `point_of_interest`
type alone cannot admit an office, restroom, or lodging. Food and shopping
types enter only when the user asks for them. The server removes duplicate
places and ranks the eligible results by its type and distance score. The
score does not measure popularity, travel time, hours, or visit duration.

The server does not store these results. Follow
[Google's Places policies](https://developers.google.com/maps/documentation/places/web-service/policies)
when you use them. Show the required attribution in a Places UI.

### Tourism

The server uses verified destination coordinates to find a NUTS 2 region with
the [Eurostat GISCO ID service](https://gisco-services.ec.europa.eu/id/). It
then gets monthly accommodation nights from Eurostat `tour_occ_nin2m`. It uses
NUTS 2024 boundaries. Some destinations are outside this data area.

If the region or data is unavailable, the level is `unknown`. For complete
data, the server compares the requested months with that year's monthly mean.
It classifies the index with POC thresholds. The response gives the region,
observation year, source URL, retrieval time, and limits of the data. Regional
accommodation nights do not forecast city crowds.

## Travel assessment jobs

Search `/api/destinations/search`. Pass the selected `open-meteo:<id>` to
`POST /api/travel-plans`. A trip can have 1 to 14 days. For example:

```json
{
  "destinationId": "open-meteo:2988507",
  "startDate": "2027-07-01",
  "endDate": "2027-07-03",
  "preferences": {
    "interests": ["history", "museums"],
    "pace": "relaxed",
    "dailyStartTime": "09:00:00",
    "dailyEndTime": "19:00:00",
    "transportMode": "walking",
    "budgetLevel": "medium",
    "avoidCrowds": true
  },
  "language": "en"
}
```

The `202 Accepted` response gives a plan ID and status and events URLs. Get
`/api/travel-plans/{id}` for status. Get `/api/travel-plans/{id}/events` for
SSE. Use `Last-Event-ID` to reconnect while the job is in memory. Send
`DELETE /api/travel-plans/{id}` to cancel a queued or running job.

Results label forecast and historical weather. They show missing evidence,
source IDs, provider warnings, and uncertainty. The attraction score measures
interest match and distance. The seasonality result uses regional data. The UI
has no map, itinerary, or controls to change a completed result.

The shared routing contract has a fixed test fixture for future itinerary
tests. This sample has no live routing provider or travel-time estimate.

### Demo Knowledge materials

The [knowledge folder](knowledge/README.md) has Markdown guides for Paris,
London, Berlin, Rome, Madrid, and Amsterdam. The server loads these guides into
a local index when it starts. It matches a guide by city and country, then
ranks its sections by the trip interests. The result includes source
IDs, source links, and the date when the source was checked. This local search
needs no embedding service or vector database. Check the source sites before
you use the guides for a real trip.

If no section matches an interest, the server uses the guide's section order.
The server stops at startup if a guide is missing or invalid.

## Validation

Run these checks from the repository root:

```bash
dotnet build samples/10-ai-travel-planner/app/ai-travel-planner.csproj
npm --prefix samples/10-ai-travel-planner/app run build
dotnet test samples/10-ai-travel-planner/test/ai-travel-planner.Tests.csproj
```

Backend tests use local HTTP fixtures. They do not need provider keys. Live
provider calls need network access. Live Places calls also need a key.

Destination data comes from [Open-Meteo Geocoding](https://open-meteo.com/en/docs/geocoding-api)
and [GeoNames](https://www.geonames.org/). Weather comes from
[Open-Meteo Forecast](https://open-meteo.com/en/docs) and
[Historical Weather](https://open-meteo.com/en/docs/historical-weather-api).
Regional tourism data comes from [Eurostat](https://ec.europa.eu/eurostat/databrowser/view/tour_occ_nin2m/).
