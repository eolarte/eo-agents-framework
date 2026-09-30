sealed class ExtractedConfirmedOrder
{
    public List<ExtractedConfirmedDish>? Dishes { get; set; }
}

sealed class ExtractedConfirmedDish
{
    public string? Name { get; set; }

    public List<ExtractedVariation>? Variations { get; set; }
}

sealed class ExtractedVariation
{
    public string? Request { get; set; }

    public string? Outcome { get; set; }
}
