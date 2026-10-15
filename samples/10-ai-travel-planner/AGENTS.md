# AI Travel Planner Agent Instructions

These rules apply to the travel planner sample and its `app/` and `test/`
subdirectories. Keep the sample as one deployable ASP.NET Core application
with a React/Vite frontend, organized as a modular monolith on both sides.

## Structure

```text
samples/10-ai-travel-planner/
├── AGENTS.md
├── README.md
├── app/
│   ├── Program.cs                         # host composition only
│   ├── Modules/
│   │   └── <Feature>/
│   │       ├── Api/
│   │       ├── Application/
│   │       ├── Domain/
│   │       └── Infrastructure/
│   └── src/
│       ├── App.jsx                        # app shell/module composition
│       └── modules/<feature>/             # feature-owned UI and behavior
└── test/                                  # focused backend tests
```

Add a module when there is implemented feature behavior to own. Do not create
empty placeholder modules for future capabilities.

## Backend module rules

- Keep `Program.cs` focused on shared host setup, static-file hosting, and
  composing modules through their registration and endpoint-mapping methods.
- A module owns its API endpoints, application contracts/use cases, domain
  types, infrastructure adapters, dependency registration, and route mapping.
- Keep dependencies inside a module directed inward: API code depends on
  application contracts; application code depends on domain types; infrastructure
  implements application contracts and may use domain types.
- Keep module composition in the module registration entry point. Do not put
  provider-specific HTTP calls in endpoints or domain types.
- Do not reference another module's implementation folders. If modules need to
  collaborate, expose a deliberate application contract and compose it at the
  host boundary.
- Preserve explicit input validation, cancellation, and provider error handling.

## Frontend module rules

- Keep cross-cutting app shell and module composition in `app/src/App.jsx` and
  the React bootstrap in `app/src/main.jsx`.
- Keep feature pages, hooks/state, API clients, formatting, and feature-specific
  components inside `app/src/modules/<feature>/`.
- A feature module must not import another feature module's internal files.
  Put deliberate shared UI or contracts at an app-level shared boundary only
  when multiple features need them.
- Keep API requests in the owning feature's API client. Keep provider and
  transport details out of presentation components.
- Preserve accessible keyboard behavior, combobox/listbox semantics, loading,
  empty, error, retry, and selected states when changing destination search.

## Hosting and data

- Vite builds the frontend into `app/wwwroot/customer-ui/`; keep this path,
  Vite's production base path, and ASP.NET Core static-file hosting aligned.
- Vite development requests under `/api` must continue to proxy to the ASP.NET
  Core app on port 5000 unless both configurations are intentionally changed.
- Keep Open-Meteo and GeoNames attribution visible in the UI and documented in
  `README.md` when destination data is used.
- Document provider configuration, environment variables, and runtime behavior
  in `README.md`. Never commit credentials or generated build output.
- Do not edit `bin/`, `obj/`, or `node_modules/` contents by hand.

## Validation

Run the targeted checks for changed layers from the repository root:

```bash
dotnet build samples/10-ai-travel-planner/app/ai-travel-planner.csproj
npm --prefix samples/10-ai-travel-planner/app run build
dotnet test samples/10-ai-travel-planner/test/ai-travel-planner.Tests.csproj
```

When frontend dependencies need installation, run `npm ci` from
`samples/10-ai-travel-planner/app/`.
