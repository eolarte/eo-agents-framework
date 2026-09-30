# Car rental policies with RAG

This sample uses an Agent Framework agent with retrieval-augmented generation
(RAG) to answer questions about rental rules for a selected vehicle category.
The agent begins by asking which type of car the customer wants. It retrieves
relevant policy text from local Markdown files before each response and cites
the source file by name.

The five files in `policies/` cover SUVs, small cars, electric cars, vans, and
luxury cars. Their rules are fictional demonstration content; they are not
legal advice or the terms of a real rental provider. Add or edit Markdown files
in that folder to change the sample corpus. The files are copied beside the
application during build and indexed in an in-memory vector store at startup.

## Run

Set an OpenAI API key and, optionally, chat and embedding model names:

```bash
export AZURE_OPENAI_API_KEY="your-api-key"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini" # optional chat model
export AZURE_OPENAI_EMBEDDING_MODEL="text-embedding-3-small" # optional
dotnet run --project samples/05-car-rental-rag/car-rental-rag.csproj
```

`AZURE_OPENAI_API_KEY` is required. The sample uses it with the OpenAI API for
both chat completion and embeddings. The model variables default to
`gpt-4o-mini` and `text-embedding-3-small`, respectively. The selected
embedding model must produce 1,536-dimensional vectors.

The conversation runs in the console until the customer types `exit` or presses
Enter. The vector index is rebuilt from the policy files on each run; no rental
booking, payment, or external rental system is connected.
