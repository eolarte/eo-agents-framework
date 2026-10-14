# Knowledge Assistant

A console based Retrieval Augmented Generation (RAG) sample using Microsoft Agent Framework. It ingests local Markdown and text files, creates embeddings, stores chunks in Qdrant, and offers direct vector search and an agent chat. It also exposes the knowledge search as an authenticated MCP tool so compatible AI agent clients can connect to the same knowledge base.

Qdrant is the demo vector database. Run it locally with Docker:

```bash
docker run --rm -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

The sample uses the Qdrant gRPC endpoint on port `6334`. Set `QDRANT_HOST` and `QDRANT_PORT` to override the defaults.

## Configure the model provider

The sample uses the OpenAI API through `Microsoft.Extensions.AI.OpenAI`. Set an API key before starting it:

```bash
export AZURE_OPENAI_API_KEY="your-openai-api-key"
```

Despite the environment variable's legacy name, this sample constructs an OpenAI client. Optionally set `AZURE_OPENAI_DEPLOYMENT_NAME` (default `gpt-4o-mini`) and `AZURE_OPENAI_EMBEDDING_MODEL` (default `text-embedding-3-small`). The embedding model must produce 1536 dimensional vectors.

## Run

```bash
dotnet run --project samples/09-knowledge-assistant/knowledge-assistant.csproj
```

The menu provides these actions:

1. **Ingest files** — recursively reads `.md` and `.txt` files in a selected folder. Enter selects the sample's `knowledge/` directory. Documents are split into overlapping 1200 character chunks and upserted using stable IDs, so re-ingesting updates existing chunks.
2. **Search vector store** — embeds a query and prints the five closest passages with source and similarity score.
3. **Chat** — starts an Agent Framework session that retrieves passages before each model response and asks the agent to cite source files.
4. **Generate API key** — generates a random bearer key, prints it once, and saves it to `~/.knowledge-assistant/api-key` for local demo use.
5. **Start MCP server** — starts an authenticated MCP Streamable HTTP server at `http://127.0.0.1:5088` (override with `KNOWLEDGE_MCP_URL`). Press Ctrl+C to stop it. Generate a key first. The server offers the `search_knowledge` tool.

The app requires a reachable Qdrant instance and model credentials even if you only want to ingest/search. Embedding generation uses the configured OpenAI service.

## Connect an AI agent with MCP

Run the sample, generate a key, then choose **Start MCP server**. Or start the MCP server directly from the command line after generating the key once:

```bash
dotnet run --project samples/09-knowledge-assistant/knowledge-assistant.csproj -- --mcp-server
```

Configure an MCP client with the server URL `http://127.0.0.1:5088` and request header `Authorization: Bearer <generated-key>`. For example, use the MCP Inspector:

```bash
npx @modelcontextprotocol/inspector
```

Choose Streamable HTTP in the Inspector and provide the URL and Authorization header. The `search_knowledge` tool can then be added to a Microsoft Agent Framework agent or another MCP compatible agent. Agent Framework's .NET MCP client can discover and call MCP server tools, allowing an agent to retrieve current knowledge on demand.

The key is a local demo credential stored in a plain text file. The server binds to loopback by default. Do not expose this sample publicly or reuse the key in production; replace its local file based key handling with managed identity or a proper authentication provider for a deployed service.

## Scope and supported files

This demo reads UTF-8 `.md` and `.txt` files only. It does not extract text from PDF, Office documents, or images. Production ingestion typically adds format specific parsers, document-level metadata and access control, improved chunking, and a managed vector database.
