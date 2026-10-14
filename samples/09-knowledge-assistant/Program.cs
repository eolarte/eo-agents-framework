using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.VectorData.Qdrant;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using OpenAI;
using Qdrant.Client;

const string CollectionName = "knowledge_chunks";
const int EmbeddingDimensions = 1536;
const int ChunkSize = 1200;
const int ChunkOverlap = 150;

var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");
}

var modelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";
var embeddingModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL") ?? "text-embedding-3-small";
var qdrantHost = Environment.GetEnvironmentVariable("QDRANT_HOST") ?? "localhost";
var qdrantPort = int.TryParse(Environment.GetEnvironmentVariable("QDRANT_PORT"), out var configuredPort)
    ? configuredPort
    : 6334;

var openAiClient = new OpenAIClient(apiKey);
var chatClient = openAiClient.GetChatClient(modelName).AsIChatClient();
var embeddingGenerator = openAiClient.GetEmbeddingClient(embeddingModelName)
    .AsIEmbeddingGenerator(EmbeddingDimensions);
var qdrantClient = new QdrantClient(qdrantHost, qdrantPort);
VectorStore vectorStore = new QdrantVectorStore(qdrantClient, ownsClient: true);
var collection = vectorStore.GetCollection<Guid, KnowledgeChunk>(CollectionName);
await collection.EnsureCollectionExistsAsync();
var knowledge = new KnowledgeService(collection, embeddingGenerator);
var keyFilePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".knowledge-assistant",
    "api-key");

if (args.Contains("--mcp-server", StringComparer.OrdinalIgnoreCase))
{
    await RunMcpServerAsync();
    return;
}

Console.WriteLine("Knowledge Assistant demo");
Console.WriteLine($"Qdrant: {qdrantHost}:{qdrantPort} | Collection: {CollectionName}");
while (true)
{
    Console.WriteLine("\n1) Ingest files  2) Search vector store  3) Chat  4) Generate API key  5) Start MCP server  0) Exit");
    Console.Write("Select: ");
    var choice = Console.ReadLine()?.Trim();

    try
    {
        switch (choice)
        {
            case "1":
                Console.Write($"Folder to ingest (Enter for '{Path.Combine(AppContext.BaseDirectory, "knowledge")}'): ");
                var folder = Console.ReadLine();
                await IngestFolderAsync(string.IsNullOrWhiteSpace(folder)
                    ? Path.Combine(AppContext.BaseDirectory, "knowledge")
                    : folder.Trim());
                break;
            case "2":
                Console.Write("Search query: ");
                var query = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(query))
                {
                    await PrintSearchAsync(query.Trim());
                }
                break;
            case "3":
                await ChatAsync();
                break;
            case "4":
                GenerateApiKey();
                break;
            case "5":
                await RunMcpServerAsync();
                break;
            case "0":
                return;
            default:
                Console.WriteLine("Choose one of the listed options.");
                break;
        }
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Operation failed: {exception.Message}");
    }
}

async Task IngestFolderAsync(string folderPath)
{
    if (!Directory.Exists(folderPath))
    {
        throw new DirectoryNotFoundException($"Folder '{folderPath}' does not exist.");
    }

    var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
        .Where(path => Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase))
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (files.Length == 0)
    {
        Console.WriteLine("No .md or .txt files found.");
        return;
    }

    var count = 0;
    foreach (var file in files)
    {
        var text = await File.ReadAllTextAsync(file);
        var chunks = SplitIntoChunks(text);
        for (var index = 0; index < chunks.Count; index++)
        {
            var id = CreateStableId(Path.GetFullPath(file), index);
            var embedding = await embeddingGenerator.GenerateAsync(chunks[index]);
            await collection.UpsertAsync(new KnowledgeChunk
            {
                Id = id,
                Source = Path.GetRelativePath(folderPath, file),
                ChunkIndex = index,
                Text = chunks[index],
                Embedding = embedding.Vector
            });
            count++;
        }
    }

    Console.WriteLine($"Indexed {count} chunks from {files.Length} files.");
}

async Task PrintSearchAsync(string query)
{
    var results = await knowledge.SearchAsync(query, 5, CancellationToken.None);
    if (results.Count == 0)
    {
        Console.WriteLine("No matching chunks found. Ingest files first.");
        return;
    }

    foreach (var result in results)
    {
        Console.WriteLine($"\n[{result.Source} · chunk {result.ChunkIndex} · score {result.Score:0.000}]");
        Console.WriteLine(result.Text);
    }
}

async Task ChatAsync()
{
    var agent = chatClient.AsAIAgent(new ChatClientAgentOptions
    {
        Name = "KnowledgeAssistant",
        ChatOptions = new ChatOptions
        {
            Instructions = "Answer using retrieved knowledge chunks. Cite each source filename. If the retrieved material does not answer the question, say so. Do not make up facts."
        },
        AIContextProviders =
        [
            new TextSearchProvider(async (text, cancellationToken) =>
            {
                var results = await knowledge.SearchAsync(text, 5, cancellationToken);
                return results.Select(result => new TextSearchProvider.TextSearchResult
                {
                    SourceName = result.Source,
                    Text = result.Text,
                    RawRepresentation = result
                });
            }, new TextSearchProviderOptions
            {
                SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke
            })
        ]
    });

    var session = await agent.CreateSessionAsync();
    Console.WriteLine("Chat started. Enter 'back' to return to the menu.");
    while (true)
    {
        Console.Write("You: ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input) || input.Equals("back", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        var response = await agent.RunAsync(input, session);
        Console.WriteLine($"Assistant: {response}");
    }
}

void GenerateApiKey()
{
    Directory.CreateDirectory(Path.GetDirectoryName(keyFilePath)!);
    var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    File.WriteAllText(keyFilePath, key, Encoding.UTF8);
    Console.WriteLine("A new MCP bearer API key was generated. Copy it now; it is stored locally for this demo.");
    Console.WriteLine($"API key: {key}");
    Console.WriteLine($"Key file: {keyFilePath}");
}

async Task RunMcpServerAsync()
{
    if (!File.Exists(keyFilePath))
    {
        throw new InvalidOperationException("Generate an API key from the menu first.");
    }

    var expectedKey = (await File.ReadAllTextAsync(keyFilePath)).Trim();
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("KNOWLEDGE_MCP_URL") ?? "http://127.0.0.1:5088");
    builder.Services.AddSingleton(knowledge);
    builder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithTools<KnowledgeMcpTools>();
    var app = builder.Build();
    app.Use(async (context, next) =>
    {
        var suppliedKey = context.Request.Headers.Authorization.ToString();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(suppliedKey),
                Encoding.UTF8.GetBytes($"Bearer {expectedKey}")))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next();
    });
    app.MapMcp();
    Console.WriteLine("MCP server listening at " + (Environment.GetEnvironmentVariable("KNOWLEDGE_MCP_URL") ?? "http://127.0.0.1:5088"));
    Console.WriteLine("Press Ctrl+C to stop the server.");
    await app.RunAsync();
}

static Guid CreateStableId(string filePath, int chunkIndex)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{filePath}:{chunkIndex}"));
    return new Guid(hash.AsSpan(0, 16));
}

static List<string> SplitIntoChunks(string text)
{
    var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    var chunks = new List<string>();
    for (var start = 0; start < normalized.Length; start += ChunkSize - ChunkOverlap)
    {
        var length = Math.Min(ChunkSize, normalized.Length - start);
        var chunk = normalized.Substring(start, length).Trim();
        if (chunk.Length > 0)
        {
            chunks.Add(chunk);
        }
    }

    return chunks;
}

public sealed class KnowledgeChunk
{
    [VectorStoreKey]
    public Guid Id { get; set; }

    [VectorStoreData]
    public string Source { get; set; } = string.Empty;

    [VectorStoreData]
    public int ChunkIndex { get; set; }

    [VectorStoreData]
    public string Text { get; set; } = string.Empty;

    [VectorStoreVector(1536)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}

public sealed record KnowledgeSearchResult(string Source, int ChunkIndex, string Text, double Score);

public sealed class KnowledgeService(
    VectorStoreCollection<Guid, KnowledgeChunk> collection,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
{
    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        string query,
        int top,
        CancellationToken cancellationToken)
    {
        var queryEmbedding = await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken);
        var results = collection.SearchAsync(queryEmbedding[0].Vector, top, cancellationToken: cancellationToken);
        var matches = new List<KnowledgeSearchResult>();
        await foreach (var result in results.WithCancellation(cancellationToken))
        {
            matches.Add(new KnowledgeSearchResult(
                result.Record.Source,
                result.Record.ChunkIndex,
                result.Record.Text,
                result.Score ?? 0));
        }

        return matches;
    }
}

[McpServerToolType]
public sealed class KnowledgeMcpTools(KnowledgeService knowledge)
{
    [McpServerTool, System.ComponentModel.Description("Search the indexed knowledge base and return relevant source passages.")]
    public async Task<string> SearchKnowledge(string query, CancellationToken cancellationToken = default)
    {
        var results = await knowledge.SearchAsync(query, 5, cancellationToken);
        return results.Count == 0
            ? "No matching passages were found."
            : string.Join("\n\n", results.Select(result => $"Source: {result.Source} (chunk {result.ChunkIndex})\n{result.Text}"));
    }
}
