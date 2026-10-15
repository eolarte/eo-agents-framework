using ai_travel_planner.Modules.Destinations.Application;

namespace ai_travel_planner.Modules.Destinations.Api;

public static class DestinationEndpoints
{
    private const int MinimumQueryLength = 2;
    private const int MaximumQueryLength = 100;

    public static IEndpointRouteBuilder MapDestinationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/destinations/search", SearchAsync);
        return endpoints;
    }

    private static async Task<IResult> SearchAsync(
        string? q,
        IDestinationSearchService destinations,
        CancellationToken cancellationToken)
    {
        var query = q?.Trim();
        if (string.IsNullOrWhiteSpace(query) || query.Length < MinimumQueryLength)
            return Results.BadRequest(new { error = "q must contain at least 2 characters." });

        if (query.Length > MaximumQueryLength)
            return Results.BadRequest(new { error = $"q must contain no more than {MaximumQueryLength} characters." });

        try
        {
            var results = await destinations.SearchAsync(query, cancellationToken);
            return Results.Ok(new { destinations = results });
        }
        catch (DestinationProviderException exception)
        {
            return Results.Problem(
                title: "Destination search is temporarily unavailable.",
                detail: exception.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
