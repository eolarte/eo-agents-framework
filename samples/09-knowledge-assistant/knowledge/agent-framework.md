# Microsoft Agent Framework

Microsoft Agent Framework is an open-source development framework for building AI agents and multi-agent workflows. In .NET, agents can be created from chat clients and extended with tools, context providers, and persistent conversation sessions.

Retrieval augmented generation, or RAG, grounds an agent's answers in external knowledge. A typical RAG flow splits source documents into chunks, creates an embedding vector for every chunk, stores the vectors and metadata in a vector database, embeds a user query, retrieves the nearest chunks, and provides those chunks to the language model as context.

The Microsoft.Extensions.VectorData abstractions let application code work with vector store collections through a common API. Qdrant is one supported vector database and can run locally in Docker for development.

Model Context Protocol (MCP) provides a standard way for AI applications to discover and call tools exposed by another process or service. A knowledge assistant can expose a search tool over MCP so compatible agent clients can retrieve the same indexed content.
