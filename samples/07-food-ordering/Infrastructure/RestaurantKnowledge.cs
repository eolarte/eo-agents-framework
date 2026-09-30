using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Agents.AI;

namespace food_ordering.Infrastructure;

public interface IRestaurantKnowledge
{
    int PolicyDocumentCount { get; }
    TextSearchProvider MenuSearchProvider { get; }
    TextSearchProvider PolicySearchProvider { get; }
}

public sealed class RestaurantKnowledge : IRestaurantKnowledge
{
    public const int EmbeddingDimensions = 1536;
    private const string CollectionName = "food-ordering-knowledge";
    private const int SearchResultCount = 10;
    private readonly VectorStoreCollection<string, RestaurantDocument> collection;
    private readonly IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator;
    private readonly RestaurantData restaurant;
    private int policyDocumentCount;

    public RestaurantKnowledge(VectorStore vectorStore, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, RestaurantData restaurant)
    {
        collection = vectorStore.GetCollection<string, RestaurantDocument>(CollectionName);
        this.embeddingGenerator = embeddingGenerator;
        this.restaurant = restaurant;
    }

    public int PolicyDocumentCount => policyDocumentCount;
    public TextSearchProvider MenuSearchProvider => new(SearchAsync("menu"), new TextSearchProviderOptions
    { SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke });
    public TextSearchProvider PolicySearchProvider => new(SearchAsync(null), new TextSearchProviderOptions
    { SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke });

    public async Task IndexAsync()
    {
        await collection.EnsureCollectionExistsAsync();
        var documents = new List<(string Id, string Text, string Source)>();
        foreach (var item in restaurant.Menu)
            documents.Add(($"menu-{item.Id}", $"Menu item: {item.Name}\nCategory: {item.Category}\nPrice: {item.Price:C}\n{item.Description}\nTags: {string.Join(", ", item.Tags)}", "menu"));

        var policyDirectory = Path.Combine(AppContext.BaseDirectory, "policies");
        var files = Directory.GetFiles(policyDirectory, "*.md").OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            documents.Add((Path.GetFileNameWithoutExtension(file), await File.ReadAllTextAsync(file), Path.GetFileName(file)));
            policyDocumentCount++;
        }

        var embeddings = await embeddingGenerator.GenerateAsync(documents.Select(document => document.Text).ToArray());
        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];
            await collection.UpsertAsync(new RestaurantDocument
            { Id = document.Id, SourceName = document.Source, Text = document.Text, Embedding = embeddings[index].Vector });
        }
    }

    private Func<string, CancellationToken, Task<IEnumerable<TextSearchProvider.TextSearchResult>>> SearchAsync(string? sourceFilter)
        => async (query, cancellationToken) =>
        {
            var queryEmbedding = await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken);
            var results = collection.SearchAsync(queryEmbedding[0].Vector, top: SearchResultCount, cancellationToken: cancellationToken);
            var matches = new List<TextSearchProvider.TextSearchResult>();
            await foreach (var result in results.WithCancellation(cancellationToken))
            {
                if (sourceFilter == "menu" && result.Record.SourceName != "menu") continue;
                if (sourceFilter is null && result.Record.SourceName == "menu") continue;
                matches.Add(new TextSearchProvider.TextSearchResult
                { SourceName = result.Record.SourceName, Text = result.Record.Text, RawRepresentation = result });
            }
            return matches;
        };
}

internal sealed class RestaurantDocument
{
    [VectorStoreKey] public string Id { get; set; } = string.Empty;
    [VectorStoreData] public string SourceName { get; set; } = string.Empty;
    [VectorStoreData] public string Text { get; set; } = string.Empty;
    [VectorStoreVector(RestaurantKnowledge.EmbeddingDimensions)] public ReadOnlyMemory<float> Embedding { get; set; }
}
