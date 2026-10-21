using ai_travel_planner.Modules.Destinations.Domain;
using ai_travel_planner.Modules.TravelPlans.Api;
using ai_travel_planner.Modules.TravelPlans.Domain;
using ai_travel_planner.Modules.TravelPlans.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ai_travel_planner.Tests;

public sealed class KnowledgeCorpusTests
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly KnowledgeCorpus _corpus = new();

    [Fact]
    public async Task Module_starts_with_embedded_guides()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddTravelPlansModule();
        using var host = builder.Build();
        await host.StartAsync();

        var corpus = host.Services.GetRequiredService<KnowledgeCorpus>();
        var result = corpus.Search(DestinationFor("Paris", "France"), Request(), RetrievedAt);

        Assert.Equal("completed", result.Status);
        Assert.Contains("demo-knowledge:paris-fr", result.SourceIds);
        await host.StopAsync();
    }

    [Theory]
    [InlineData("Paris", "France", "demo-knowledge:paris-fr")]
    [InlineData("London", "United Kingdom", "demo-knowledge:london-gb")]
    [InlineData("Berlin", "Germany", "demo-knowledge:berlin-de")]
    [InlineData("Rome", "Italy", "demo-knowledge:rome-it")]
    [InlineData("Madrid", "Spain", "demo-knowledge:madrid-es")]
    [InlineData("Amsterdam", "Netherlands", "demo-knowledge:amsterdam-nl")]
    public void Returns_guide_for_each_supported_city(string city, string country, string sourceId)
    {
        var result = _corpus.Search(DestinationFor(city, country), Request(), RetrievedAt);

        Assert.Equal("completed", result.Status);
        Assert.Contains(sourceId, result.SourceIds);
        Assert.NotNull(result.Data);
    }

    [Theory]
    [InlineData("Paris", "Germany")]
    [InlineData("Lisbon", "Portugal")]
    public void Requires_matching_city_and_country(string city, string country)
    {
        var result = _corpus.Search(DestinationFor(city, country), Request(), RetrievedAt);

        Assert.Equal("not_found", result.Status);
        Assert.Empty(result.SourceIds);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Matches_city_and_country_with_case_and_space_normalization()
    {
        var result = _corpus.Search(DestinationFor(" paris ", " FRANCE "), Request(), RetrievedAt);

        Assert.Equal("completed", result.Status);
        Assert.Contains("demo-knowledge:paris-fr", result.SourceIds);
    }

    [Fact]
    public void Ranks_matching_preference_and_keeps_sources_consistent_with_evidence()
    {
        var preferences = new TravelerPreferences("slow", new TimeOnly(9, 0), new TimeOnly(17, 0),
            "walk", "mid", false, ["outdoor", "river"]);
        var result = _corpus.Search(DestinationFor("Paris", "France"), Request(preferences), RetrievedAt);

        Assert.Contains("[demo-knowledge:paris-fr | checked ", result.Data);
        Assert.Contains(" | Outdoor time |", result.Data);
        Assert.Contains("https://parisjetaime.com/eng/article/the-essentials-a264", result.Data);
        Assert.Contains("demo-knowledge:paris-fr", result.SourceIds);
        Assert.True(result.Data!.Length <= KnowledgeCorpus.MaximumEvidenceCharacters);
        Assert.All(result.SourceIds, id => Assert.Contains($"[{id} |", result.Data, StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_malformed_or_duplicate_guides()
    {
        var malformed = "# Paris, France\n\n## Art\nSome text";
        Assert.Throws<InvalidOperationException>(() => new KnowledgeCorpus([malformed, malformed, malformed, malformed, malformed, malformed]));
    }

    private static Destination DestinationFor(string city, string country) =>
        new("destination", city, country, null, 48.85, 2.35, "Europe/Paris");

    private static CreateTravelPlanRequest Request(TravelerPreferences? preferences = null) =>
        new("destination", new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 24), preferences);
}
