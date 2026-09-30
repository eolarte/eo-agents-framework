# Food ordering with Agent Framework

This end-to-end sample combines several Microsoft Agent Framework concepts in
one fictional restaurant experience:

- A coordinator delegates to menu, policy, checkout/payment, delivery, and order
  status agents. DevUI lists the coordinator and every specialist.
- Function tools search the structured menu and create local order drafts.
- A local MCP server exposes read-only restaurant hours and delivery
  information over an in-memory MCP transport.
- RAG searches embedded menu descriptions and local policy Markdown files in
  an in-memory vector store.
- `FileMemoryProvider` gives each customer a separate persistent memory folder.
- Optional OpenTelemetry export sends agent traces and metrics over OTLP to an
  Aspire Dashboard or another OTLP receiver.

Checkout is a demonstration only. The sample accepts `demo-card` or `cash` as
labels, never collects payment credentials, does not charge a payment method,
and does not place a real restaurant or courier order. Order, payment, delivery,
and lifecycle event records are stored in a local SQLite database. A background
demo worker advances confirmed orders through preparation and delivery; these
statuses are not connected to a restaurant or courier. Customer preference
files remain stored under the operating system's application-data directory.

## Run the sample

Build the customer chat frontend first. The Vite production files are written
to `wwwroot/customer-ui/` and copied beside the ASP.NET app on build. The
dedicated subfolder keeps the customer page separate from the `index.html`
embedded by the DevUI package.

```bash
cd samples/07-food-ordering
npm ci
npm run build
cd ../..
```

Set an OpenAI API key and optionally choose chat and embedding models:

```bash
export AZURE_OPENAI_API_KEY="your-api-key"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini" # optional chat model
export AZURE_OPENAI_EMBEDDING_MODEL="text-embedding-3-small" # optional
export FOOD_ORDERING_DATABASE_PATH="/path/to/food-ordering-orders.db" # optional
export FOOD_ORDERING_SIMULATION_MIN_DELAY_SECONDS="10" # optional, default 10
export FOOD_ORDERING_SIMULATION_MAX_DELAY_SECONDS="30" # optional, default 30
export ASPNETCORE_ENVIRONMENT="Development" # enables DevUI
dotnet run --project samples/07-food-ordering/food-ordering.csproj --urls http://localhost:5000
```

By default, the SQLite file is created at
`<local application data>/Microsoft/AgentFramework/food-ordering-memory/food-ordering-orders.db`.
Set `FOOD_ORDERING_DATABASE_PATH` to override the file path. The two simulation
delay settings are independently randomized for each lifecycle milestone and
must be whole numbers from 1 to 3600 seconds; the minimum cannot exceed the
maximum. The default 10–30 second range lets the full delivery simulation
finish in roughly 30–90 seconds.

Open http://localhost:5000 for the sample customer chat or
http://localhost:5000/devui for DevUI. Enter a customer name or ID in the chat
page to keep that customer's memory separate. Use different IDs to inspect
profiles independently. The in-memory vector index is rebuilt from the sample
menu and `policies/*.md` on startup.

For frontend-only iteration, run `npm run dev` from `samples/07-food-ordering`
while the ASP.NET sample is running at `http://localhost:5000`. Vite serves the
React app and proxies `/api` calls to the sample backend. Rebuild the frontend
with `npm run build` before launching the ASP.NET app to view the production UI.

To draft an order, ask for menu options, select items, and specify `demo-card`
or `cash`. Then reply `confirm` or `cancel`; use `status` to inspect the latest
active order. Each draft receives an order ID, which can be included in a status
question to inspect an older order. Confirmed orders progress through preparing,
ready, out for delivery, and delivered. `demo-card` records a simulated approval
only; `cash` remains due on delivery and is never recorded as collected. These
statuses do not contact a restaurant, payment processor, or delivery service.

## Aspire Dashboard telemetry

Start a standalone Aspire Dashboard with Docker, then set the OTLP endpoint
before launching the sample. The dashboard prints its login token in the
container logs. Open its UI at http://localhost:18888.

```bash
docker run --rm -it -p 18888:18888 -p 4317:18889 -p 4318:18890 -d --name aspire-dashboard mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

For the dashboard's local gRPC receiver:

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:4317"
export OTEL_EXPORTER_OTLP_PROTOCOL="grpc"
```

The sample only registers the OpenTelemetry exporters when
`OTEL_EXPORTER_OTLP_ENDPOINT` is set. Agent and chat-client instrumentation is
enabled with metadata by default. It does not opt into exporting conversation
messages or payment inputs. See the [Aspire Dashboard standalone setup](https://aspire.dev/dashboard/standalone/)
for other launch options and configuration.

## Customer memory

Customer files are saved below:

```text
<local application data>/Microsoft/AgentFramework/food-ordering-memory/customers/
```

The customer ID is normalized and hashed into a storage folder name. Memory
contents can include customer preferences and should be treated as private.
The sample does not provide a profile deletion UI; remove a customer's folder
to clear that local profile.

Order history is kept separately in `food-ordering-orders.db` and survives app
restarts. Remove that database file to clear local order history. Use the
`FOOD_ORDERING_DATABASE_PATH` setting to choose another location.
