using ai_travel_planner.Modules.Attractions.Application;

namespace ai_travel_planner.Modules.Attractions.Api;

public static class AttractionsEndpoints
{
    private const int MaximumDestinationIdLength = 160;
    private const int MaximumInterestsLength = 240;
    private const int MaximumInterestCount = 12;

    public static IEndpointRouteBuilder MapAttractionsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/attractions", SearchAsync);
        return endpoints;
    }

    private static async Task<IResult> SearchAsync(
        string? destinationId,
        double? latitude,
        double? longitude,
        string? interests,
        IAttractionsProvider provider,
        CancellationToken cancellationToken)
    {
        var id = destinationId?.Trim();
        if (string.IsNullOrWhiteSpace(id) || id.Length > MaximumDestinationIdLength)
            return Results.BadRequest(new { error = $"destinationId is required and must be no more than {MaximumDestinationIdLength} characters." });
        if (latitude is null || !double.IsFinite(latitude.Value) || latitude is < -90 or > 90)
            return Results.BadRequest(new { error = "latitude must be between -90 and 90." });
        if (longitude is null || !double.IsFinite(longitude.Value) || longitude is < -180 or > 180)
            return Results.BadRequest(new { error = "longitude must be between -180 and 180." });
        if (interests is { Length: > MaximumInterestsLength })
            return Results.BadRequest(new { error = $"interests must be no more than {MaximumInterestsLength} characters." });

        var parsedInterests = (interests ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parsedInterests.Length > MaximumInterestCount)
            return Results.BadRequest(new { error = $"interests must contain no more than {MaximumInterestCount} entries." });
        if (parsedInterests.Any(value => value.Length > 40))
            return Results.BadRequest(new { error = "each interest must be no more than 40 characters." });

        try
        {
            var attractions = await provider.SearchAsync(
                new AttractionsQuery(id, latitude.Value, longitude.Value, parsedInterests), cancellationToken);
            return Results.Ok(new { destinationId = id, attractions });
        }
        catch (AttractionsProviderConfigurationException exception)
        {
            return Results.Problem(title: "Attractions provider is not configured.", detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (AttractionsProviderException exception)
        {
            return Results.Problem(title: "Attraction search is temporarily unavailable.", detail: exception.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
