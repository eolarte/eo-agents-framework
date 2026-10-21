using System.Globalization;
using ai_travel_planner.Modules.Weather.Application;

namespace ai_travel_planner.Modules.Weather.Api;

public static class WeatherEndpoints
{
    public static IEndpointRouteBuilder MapWeatherEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/weather", GetAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        string? destinationId, string? latitude, string? longitude, string? timezone,
        string? startDate, string? endDate, IWeatherAssessmentService weather, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destinationId) || string.IsNullOrWhiteSpace(timezone) ||
            !double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ||
            !DateOnly.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !DateOnly.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            return Results.BadRequest(new { error = "destinationId, latitude, longitude, timezone, startDate and endDate are required; dates must use yyyy-MM-dd." });

        try
        {
            return Results.Ok(await weather.GetAsync(destinationId.Trim(), new WeatherLocation(lat, lon, timezone.Trim()), start, end, cancellationToken));
        }
        catch (WeatherRequestException exception) { return Results.BadRequest(new { error = exception.Message }); }
        catch (WeatherProviderException exception)
        {
            return Results.Problem(title: "Weather data is temporarily unavailable.", detail: exception.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
