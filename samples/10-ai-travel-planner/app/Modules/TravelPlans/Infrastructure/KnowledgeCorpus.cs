using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ai_travel_planner.Modules.Destinations.Domain;
using ai_travel_planner.Modules.TravelPlans.Domain;

namespace ai_travel_planner.Modules.TravelPlans.Infrastructure;

public sealed class KnowledgeCorpus
{
    public const int MaximumEvidenceCharacters = 1200;
    private const string ResourcePrefix = "ai_travel_planner.Knowledge.";
    private readonly IReadOnlyList<Guide> _guides;

    public KnowledgeCorpus() : this(LoadEmbeddedDocuments()) { }

    public KnowledgeCorpus(IEnumerable<string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var guides = documents.Select(Parse).ToArray();
        if (guides.Length != 6)
            throw new InvalidOperationException($"The Knowledge corpus must contain exactly six city guides. Found {guides.Length}.");
        var duplicate = guides.GroupBy(g => Normalize(g.City) + "|" + Normalize(g.Country), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"The Knowledge corpus contains duplicate destination '{duplicate.First().City}, {duplicate.First().Country}'.");
        var duplicateId = guides.GroupBy(g => g.DocumentId, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
            throw new InvalidOperationException($"The Knowledge corpus contains duplicate document ID '{duplicateId.Key}'.");
        _guides = guides;
    }

    public ResearchResult<string> Search(Destination destination, CreateTravelPlanRequest request, DateTimeOffset retrievedAt)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(destination.Country))
            return NotFound(retrievedAt);
        var guide = _guides.SingleOrDefault(g => Normalize(g.City) == Normalize(destination.Name) &&
            Normalize(g.Country) == Normalize(destination.Country));
        if (guide is null) return NotFound(retrievedAt);

        var terms = PreferenceTerms(request.Preferences);
        var selected = guide.Sections.Select((section, index) => new
            {
                Section = section,
                Index = index,
                Score = section.Terms.Count(terms.Contains)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .Take(3)
            .ToArray();
        var evidence = new StringBuilder();
        foreach (var item in selected)
        {
            var heading = $"[{guide.DocumentId} | checked {guide.CheckedDate} | {item.Section.Heading} | {guide.SourceUrls}]\n";
            var separator = evidence.Length == 0 ? string.Empty : "\n\n";
            var available = MaximumEvidenceCharacters - evidence.Length - separator.Length - heading.Length;
            if (available <= 0) break;
            var body = item.Section.Text.Length <= available ? item.Section.Text : item.Section.Text[..available].TrimEnd();
            if (body.Length == 0) break;
            evidence.Append(separator).Append(heading).Append(body);
            if (body.Length < item.Section.Text.Length) break;
        }
        var text = evidence.ToString();
        var sourceIds = text.Length == 0 ? [] : new[] { guide.DocumentId };
        return new("completed", "medium", sourceIds, retrievedAt, [], text);
    }

    private static ResearchResult<string> NotFound(DateTimeOffset retrievedAt) =>
        new("not_found", "unknown", [], retrievedAt, ["No matching destination guide was found."], null);

    private static IEnumerable<string> LoadEmbeddedDocuments()
    {
        var assembly = typeof(KnowledgeCorpus).Assembly;
        var names = assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        if (names.Length != 6)
            throw new InvalidOperationException($"Expected exactly six embedded Knowledge city guides; found {names.Length}.");
        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded Knowledge resource '{name}' is missing.");
            using var reader = new StreamReader(stream);
            yield return reader.ReadToEnd();
        }
    }

    private static Guide Parse(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) throw new InvalidOperationException("A Knowledge guide is empty.");
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var title = lines.FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim();
        var pair = title?.Split(',', 2, StringSplitOptions.TrimEntries);
        if (pair is not { Length: 2 } || pair.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("A Knowledge guide must start with '# City, Country'.");
        var id = Metadata(lines, "Document ID");
        var checkedDateText = Metadata(lines, "Source checked");
        if (!DateOnly.TryParse(checkedDateText, out var checkedDate))
            throw new InvalidOperationException($"Knowledge guide '{id}' has an invalid Source checked date.");
        var sourceLine = lines.FirstOrDefault(line => Regex.IsMatch(line, @"^- Sources?:\s*\S", RegexOptions.CultureInvariant));
        var urls = sourceLine is null ? [] : Regex.Matches(sourceLine, @"\((https?://[^)\s]+)\)")
            .Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
        if (string.IsNullOrWhiteSpace(id) || urls.Length == 0)
            throw new InvalidOperationException($"Knowledge guide '{pair[0]}' must contain a document ID and at least one source URL.");

        var sections = new List<Section>();
        string? heading = null;
        var body = new StringBuilder();
        void AddSection()
        {
            var text = body.ToString().Trim();
            if (heading is not null && text.Length > 0)
                sections.Add(new Section(heading, text, Tokenize(heading + " " + text)));
            body.Clear();
        }
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                AddSection();
                heading = line[3..].Trim();
                if (heading.Length == 0) throw new InvalidOperationException($"Knowledge guide '{id}' has an empty section heading.");
            }
            else if (heading is not null)
            {
                if (line.StartsWith("#", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Knowledge guide '{id}' has an unsupported nested heading.");
                if (line.Length > 0) body.AppendLine(line.Trim());
                else if (body.Length > 0 && body[^1] != '\n') body.AppendLine();
            }
        }
        AddSection();
        if (sections.Count == 0) throw new InvalidOperationException($"Knowledge guide '{id}' has no non-empty sections.");
        return new Guide(pair[0], pair[1], id, checkedDate, string.Join(", ", urls), sections);
    }

    private static string Metadata(IEnumerable<string> lines, string key)
    {
        var prefix = $"- {key}: ";
        var values = lines.Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => line[prefix.Length..].Trim())
            .Select(value => value.Length >= 2 && value[0] == '`' && value[^1] == '`' ? value[1..^1] : value).ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]))
            throw new InvalidOperationException($"A Knowledge guide must contain one valid '{key}' field.");
        return values[0];
    }

    private static HashSet<string> PreferenceTerms(TravelerPreferences? preferences)
    {
        if (preferences is null) return [];
        var input = string.Join(' ', preferences.Pace, preferences.TransportMode, preferences.BudgetLevel,
            preferences.AvoidCrowds ? "avoid crowds" : string.Empty, string.Join(' ', preferences.Interests ?? []));
        return Tokenize(input);
    }

    private static HashSet<string> Tokenize(string text) => Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]{3,}")
        .Select(match => match.Value).ToHashSet(StringComparer.Ordinal);

    private static string Normalize(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private sealed record Guide(string City, string Country, string DocumentId, DateOnly CheckedDate,
        string SourceUrls, IReadOnlyList<Section> Sections);
    private sealed record Section(string Heading, string Text, HashSet<string> Terms);
}

internal sealed class KnowledgeCorpusStartupValidation(KnowledgeCorpus corpus) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = corpus;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
