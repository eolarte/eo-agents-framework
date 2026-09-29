# Agent Context — .NET Samples

This repository contains small, standalone .NET sample projects for the
Microsoft Agent Framework. Create new samples under `samples/` and keep each
one self-contained.

## Repository layout

```text
.
├── AGENTS.md
├── .local/                         # Git-ignored agent working files
└── samples/
  └── <sample-project>/
    ├── Program.cs
    ├── README.md
    └── <sample-project>.csproj
```

Each sample is an independent console application. Build and run the project
being changed rather than assuming a repository-wide command.

## Local agent workspace

`.local/` is reserved for draft materials, investigation notes, temporary
artifacts, and other working files created during agentic sessions. Its
contents are intentionally excluded from Git.

Create a descriptive kebab-case subfolder for each investigation or work
stream, for example:

```text
.local/
└── performance-issue-investigation/
```

Do not store secrets or credentials in `.local/`. Move any durable project
documentation or source changes into the tracked repository files instead.

## Build and run

Sample projects target `net10.0` and declare their own Agent Framework and
Microsoft Extensions AI package dependencies.

```bash
dotnet build samples/<sample-project>/<sample-project>.csproj
dotnet run --project samples/<sample-project>/<sample-project>.csproj
```

Document each sample's provider configuration, environment variables, and
runtime behavior in its own `README.md`. Never commit API keys, endpoint
credentials, generated binaries, or environment-specific configuration.
`bin/` and `obj/` are build output and should not be edited.

## Change guidelines

- Keep each sample self-contained unless a shared abstraction is clearly
  required.
- Follow the existing top-level-program style, nullable reference types, and
  implicit usings.
- Preserve explicit failures for missing configuration; do not add silent
  fallbacks that make provider or authentication errors difficult to diagnose.
- Keep provider-specific behavior and environment-variable names documented in
  the corresponding sample README.
- Avoid changing preview package versions unless the change specifically
  requires a coordinated dependency update.

## Validation

After changing a sample, run its targeted build:

```bash
dotnet build samples/<sample-project>/<sample-project>.csproj
```

Run the sample manually when the change affects runtime behavior and the
required provider credentials are available.
