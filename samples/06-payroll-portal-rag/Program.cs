using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using OpenAI;
using CommunityToolkit.VectorData.InMemory;

const string CollectionName = "payroll-policies-and-statement";
const int EmbeddingDimensions = 1536;
const int SearchResultCount = 4;

var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");
}

var modelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME")
    ?? "gpt-4o-mini";
var embeddingModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL")
    ?? "text-embedding-3-small";
var openAiClient = new OpenAIClient(apiKey);
var embeddingGenerator = openAiClient
    .GetEmbeddingClient(embeddingModelName)
    .AsIEmbeddingGenerator(EmbeddingDimensions);

VectorStore vectorStore = new InMemoryVectorStore(new()
{
    EmbeddingGenerator = embeddingGenerator
});
var payrollCollection = vectorStore.GetCollection<string, PayrollDocumentRecord>(CollectionName);
await payrollCollection.EnsureCollectionExistsAsync();

var policyDirectory = Path.Combine(AppContext.BaseDirectory, "policies");
var policyFiles = Directory.GetFiles(policyDirectory, "*.md")
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (policyFiles.Length == 0)
{
    throw new InvalidOperationException($"No payroll documents were found in '{policyDirectory}'.");
}

foreach (var policyFile in policyFiles)
{
    var policyText = await File.ReadAllTextAsync(policyFile);
    var embedding = await embeddingGenerator.GenerateAsync(policyText);

    await payrollCollection.UpsertAsync(new PayrollDocumentRecord
    {
        Id = Path.GetFileNameWithoutExtension(policyFile),
        SourceName = Path.GetFileName(policyFile),
        Text = policyText,
        Embedding = embedding.Vector
    });
}

async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchPayrollDocumentsAsync(
    string query,
    CancellationToken cancellationToken)
{
    var queryEmbedding = await embeddingGenerator.GenerateAsync(
        [query],
        cancellationToken: cancellationToken);
    var results = payrollCollection.SearchAsync(
        queryEmbedding[0].Vector,
        top: SearchResultCount,
        cancellationToken: cancellationToken);
    var searchResults = new List<TextSearchProvider.TextSearchResult>();

    await foreach (var result in results.WithCancellation(cancellationToken))
    {
        searchResults.Add(new TextSearchProvider.TextSearchResult
        {
            SourceName = result.Record.SourceName,
            Text = result.Record.Text,
            RawRepresentation = result
        });
    }

    return searchResults;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddAIAgent("payroll-assistant", (_, name) =>
    openAiClient
        .GetChatClient(modelName)
        .AsIChatClient()
        .AsAIAgent(new ChatClientAgentOptions
        {
            Name = name,
            ChatOptions = new ChatOptions
            {
                Instructions =
                    "You are a helpful payroll portal assistant for one fictional demo employee, " +
                    "Alex Demo. Explain the employee's pay statement using the retrieved payroll " +
                    "documents. Cite the source Markdown filename for policy explanations. Keep " +
                    "the pay figures and arithmetic consistent with the payroll statement. Do not " +
                    "invent pay components, tax rules, or personal data. If the documents do not " +
                    "cover a question, say so. The company, employee, currency, country, policies, " +
                    "and figures are fictional demonstration content, not real payroll or tax advice."
            },
            AIContextProviders =
            [
                new TextSearchProvider(SearchPayrollDocumentsAsync, new TextSearchProviderOptions
                {
                    SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke
                })
            ]
        }));

builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();
builder.Services.AddDevUI();

var app = builder.Build();
app.MapOpenAIResponses();
app.MapOpenAIConversations();

if (app.Environment.IsDevelopment())
{
    app.MapDevUI();
}

Console.WriteLine("Fictional payroll portal demo");
if (app.Environment.IsDevelopment())
{
    Console.WriteLine("DevUI is available at /devui.");
}
else
{
    Console.WriteLine("DevUI is disabled. Set ASPNETCORE_ENVIRONMENT=Development to enable it.");
}
Console.WriteLine($"Loaded {policyFiles.Length} payroll documents into the in-memory search index.");
Console.WriteLine("The sample's company, Exampleland rules, employee, and amounts are fictional.");
await app.RunAsync();

public sealed class PayrollDocumentRecord
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData]
    public string SourceName { get; set; } = string.Empty;

    [VectorStoreData]
    public string Text { get; set; } = string.Empty;

    [VectorStoreVector(1536)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
