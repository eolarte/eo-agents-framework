# Agent Framework quick start sample

This sample supports either a direct OpenAI API key or Azure AI Foundry credentials.

## OpenAI

Set the API key in the shell before running the app:

```bash
export AZURE_OPENAI_API_KEY="your-api-key"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini" # optional
dotnet run
```

`AZURE_OPENAI_API_KEY` is read at runtime and is not stored in the project.

## Azure AI Foundry

If `AZURE_OPENAI_API_KEY` is not set, the sample uses `DefaultAzureCredential`:

```bash
export AZURE_OPENAI_ENDPOINT="https://your-project-endpoint"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini" # optional
az login
dotnet run
```
