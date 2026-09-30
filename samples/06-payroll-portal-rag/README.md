# Payroll portal with RAG and DevUI

This sample hosts a Microsoft Agent Framework payroll assistant in ASP.NET Core
and exposes it through DevUI. The assistant answers questions about one
fictional demo employee's May 2026 pay statement. Before each response, it
retrieves relevant passages from local Markdown files covering company
payroll rules, fictional Exampleland tax rules, and the statement itself. It
should cite policy source filenames when explaining how a calculation works.

The company, employee, country, currency, policies, and figures are fictional.
The invented payroll credits (PC) have no real-world value, and the simplified
Exampleland tax rules are not tax advice. Edit the Markdown files in `policies/`
to change the indexed demo content. Files are copied beside the app and indexed
in an in-memory vector store at startup.

## Run

Set an OpenAI API key and, optionally, chat and embedding model names:

```bash
export AZURE_OPENAI_API_KEY="your-api-key"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini" # optional chat model
export AZURE_OPENAI_EMBEDDING_MODEL="text-embedding-3-small" # optional
export ASPNETCORE_ENVIRONMENT="Development" # required to enable DevUI
dotnet run --project samples/06-payroll-portal-rag/payroll-portal-rag.csproj --urls http://localhost:5000
```

`AZURE_OPENAI_API_KEY` is required. The sample uses the key with the OpenAI API
for both chat completion and embeddings. The model variables default to
`gpt-4o-mini` and `text-embedding-3-small`; the embedding model must return
1,536-dimensional vectors. Open http://localhost:5000/devui. DevUI is only
mapped in Development, so set `ASPNETCORE_ENVIRONMENT=Development` before
running the sample.

The DevUI host also maps the OpenAI Responses and Conversations endpoints used
by DevUI. The sample rebuilds its in-memory index at startup and does not
connect to an identity provider, employee database, or payroll system.
