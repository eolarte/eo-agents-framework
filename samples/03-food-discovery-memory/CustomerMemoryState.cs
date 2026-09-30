using System.Text.Json.Serialization;

sealed class CustomerMemoryState
{
    public string? CustomerName { get; set; }

    public List<string> DietaryPreferences { get; set; } = [];

    public List<string> FavoriteCuisines { get; set; } = [];

    public List<string> DislikedFoods { get; set; } = [];

    public string? SpiceTolerance { get; set; }

    public List<SavedOrder> OrderHistory { get; set; } = [];

    [JsonConverter(typeof(SafeNullableDecimalConverter))]
    public decimal? TypicalBudget { get; set; }

    public void MergeFrom(CustomerMemoryState other)
    {
        CustomerName ??= other.CustomerName;
        DietaryPreferences = MergeValues(
            DietaryPreferences,
            other.DietaryPreferences);
        FavoriteCuisines = MergeValues(
            FavoriteCuisines,
            other.FavoriteCuisines);
        DislikedFoods = MergeValues(
            DislikedFoods,
            other.DislikedFoods);
        SpiceTolerance ??= other.SpiceTolerance;
        OrderHistory = [.. OrderHistory, .. other.OrderHistory];
        TypicalBudget ??= other.TypicalBudget;
    }

    public void MergeFrom(ExtractedCustomerPreferences other)
    {
        CustomerName ??= CleanValue(other.CustomerName);
        DietaryPreferences = MergeValues(
            DietaryPreferences,
            other.DietaryPreferences);
        FavoriteCuisines = MergeValues(
            FavoriteCuisines,
            other.FavoriteCuisines);
        DislikedFoods = MergeValues(
            DislikedFoods,
            other.DislikedFoods);
        SpiceTolerance ??= CleanValue(other.SpiceTolerance);
        TypicalBudget ??= other.TypicalBudget;
    }

    private static List<string> MergeValues(
        IEnumerable<string> existing,
        IEnumerable<string>? additions)
    {
        return existing
            .Concat(additions ?? [])
            .Select(CleanValue)
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? CleanValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

sealed record SavedOrder(
    string CustomerRequest,
    string AgentResponse,
    DateTimeOffset ConfirmedAtUtc);
