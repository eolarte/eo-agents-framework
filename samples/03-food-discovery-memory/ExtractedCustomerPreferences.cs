using System.Text.Json.Serialization;

sealed class ExtractedCustomerPreferences
{
    public List<string>? DietaryPreferences { get; set; }

    public List<string>? FavoriteCuisines { get; set; }

    public List<string>? DislikedFoods { get; set; }

    public string? SpiceTolerance { get; set; }

    [JsonConverter(typeof(SafeNullableDecimalConverter))]
    public decimal? TypicalBudget { get; set; }
}
