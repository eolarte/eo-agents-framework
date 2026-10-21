using System.Net;
using System.Text;
using ai_travel_planner.Modules.Destinations.Domain;
using ai_travel_planner.Modules.Tourism.Application;
using ai_travel_planner.Modules.Tourism.Domain;
using ai_travel_planner.Modules.Tourism.Infrastructure;
using Xunit;

namespace ai_travel_planner.Tests;

public sealed class TourismTests
{
    private static readonly Destination Paris = new("open-meteo:2988507", "Paris", "France", null,
        48.8566, 2.3522, "Europe/Paris");

    [Fact]
    public async Task Destination_outside_nuts_coverage_remains_unknown_without_eurostat_call()
    {
        var provider = new StubTourismProvider();
        var regionResolver = new StubRegionResolver(null);
        var service = new TourismAssessmentService(provider, regionResolver);

        var result = await service.AssessAsync(
            Paris, new DateOnly(2027, 7, 1), new DateOnly(2027, 7, 3), CancellationToken.None);

        Assert.Equal("unknown", result.Level);
        Assert.Equal("unknown", result.Confidence);
        Assert.Null(result.MonthlyIndex);
        Assert.False(provider.Called);
        Assert.Equal(48.8566, regionResolver.Latitude);
        Assert.Equal(2.3522, regionResolver.Longitude);
    }

    [Fact]
    public async Task Complete_regional_data_produces_index_with_caveat()
    {
        var observations = Enumerable.Range(1, 12).ToDictionary(month => month, month => month == 7 ? 240d : 100d);
        var provider = new StubTourismProvider(new TourismStatistics(
            "FR10", 2025, observations, "https://ec.europa.eu/example", DateTimeOffset.Parse("2026-10-01T00:00:00Z")));
        var service = new TourismAssessmentService(provider, new StubRegionResolver("FR10"));

        var result = await service.AssessAsync(
            Paris, new DateOnly(2027, 7, 1), new DateOnly(2027, 7, 3), CancellationToken.None);

        Assert.Equal("peak", result.Level);
        Assert.Equal("low", result.Confidence);
        Assert.Equal("FR10", result.GeographyCode);
        Assert.Equal(2025, result.ObservationYear);
        Assert.Contains(result.Warnings, warning => warning.Contains("proxy"));
    }

    [Fact]
    public async Task Transient_provider_failure_is_retried_once()
    {
        var observations = Enumerable.Range(1, 12).ToDictionary(month => month, _ => 100d);
        var provider = new FlakyTourismProvider(new TourismStatistics(
            "FR10", 2025, observations, "https://ec.europa.eu/example", DateTimeOffset.Parse("2026-10-01T00:00:00Z")));
        var service = new TourismAssessmentService(provider, new StubRegionResolver("FR10"));

        var result = await service.AssessAsync(
            Paris, new DateOnly(2027, 7, 1), new DateOnly(2027, 7, 3), CancellationToken.None);

        Assert.Equal(2, provider.Calls);
        Assert.Equal("completed", result.Status);
    }

    [Fact]
    public async Task Gisco_lookup_uses_verified_coordinates_and_nuts_two_level()
    {
        using var handler = new StubHandler(_ => JsonResponse("""
            [{"attributes":{"nuts_id":"FR10","stat_levl_code":2,"OBJECTID":"FR10"}}]
            """));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://gisco-services.ec.europa.eu/") };
        var resolver = new GiscoNuts2RegionResolver(client);

        var code = await resolver.ResolveAsync(48.8566, 2.3522, CancellationToken.None);

        Assert.Equal("FR10", code);
        Assert.Equal("/id/nuts/", handler.LastUri!.AbsolutePath);
        Assert.Contains("x=2.3522", handler.LastUri.Query);
        Assert.Contains("y=48.8566", handler.LastUri.Query);
        Assert.Contains("year=2024", handler.LastUri.Query);
        Assert.Contains("level=2", handler.LastUri.Query);
        Assert.Contains("proj=4326", handler.LastUri.Query);
    }

    [Fact]
    public async Task Gisco_lookup_does_not_accept_other_nuts_levels()
    {
        using var handler = new StubHandler(_ => JsonResponse("""
            [{"attributes":{"nuts_id":"FR101","stat_levl_code":3,"OBJECTID":"FR101"}}]
            """));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://gisco-services.ec.europa.eu/") };
        var resolver = new GiscoNuts2RegionResolver(client);

        Assert.Null(await resolver.ResolveAsync(48.8566, 2.3522, CancellationToken.None));
    }

    [Fact]
    public async Task Gisco_no_coverage_response_remains_unknown()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("""
                {"error":"Server Error","status":500,"details":"Failed to convert GeoJSON to ArcGIS format: Cannot read properties of null (reading 'length')"}
                """, Encoding.UTF8, "application/json")
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://gisco-services.ec.europa.eu/") };
        var service = new TourismAssessmentService(new StubTourismProvider(), new GiscoNuts2RegionResolver(client));

        var result = await service.AssessAsync(Paris, new DateOnly(2027, 7, 1),
            new DateOnly(2027, 7, 3), CancellationToken.None);

        Assert.Equal("unknown", result.Level);
        Assert.Null(result.GeographyCode);
    }

    [Fact]
    public async Task Transient_gisco_failure_is_retried_once()
    {
        var observations = Enumerable.Range(1, 12).ToDictionary(month => month, _ => 100d);
        var resolver = new FlakyRegionResolver();
        var service = new TourismAssessmentService(new StubTourismProvider(new TourismStatistics(
            "FR10", 2025, observations, "https://ec.europa.eu/example", DateTimeOffset.Parse("2026-10-01T00:00:00Z"))), resolver);

        var result = await service.AssessAsync(Paris, new DateOnly(2027, 7, 1),
            new DateOnly(2027, 7, 3), CancellationToken.None);

        Assert.Equal(2, resolver.Calls);
        Assert.Equal("completed", result.Status);
    }

    [Fact]
    public async Task Eurostat_adapter_selects_latest_complete_year_from_json_stat()
    {
        var values = string.Join(',', Enumerable.Range(0, 24)
            .Select(index => $"\"{index}\":{(index < 12 ? 50 : 100 + index)}"));
        var json = """
            {"id":["time","month"],"size":[2,12],"dimension":{
              "time":{"category":{"index":{"2024":0,"2025":1}}},
              "month":{"category":{"index":{"M01":0,"M02":1,"M03":2,"M04":3,"M05":4,"M06":5,"M07":6,"M08":7,"M09":8,"M10":9,"M11":10,"M12":11}}}},
              "value":{
            """ + values + "}}";
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://ec.europa.eu/eurostat/") };
        var provider = new EurostatTourismStatisticsProvider(client, TimeProvider.System);

        var result = await provider.GetAsync("FR10", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2025, result.ObservationYear);
        Assert.Equal(112d, result.NightsByMonth[1]);
        Assert.Contains("geo=FR10", handler.LastUri!.Query);
    }

    private sealed class StubRegionResolver(string? code) : INuts2RegionResolver
    {
        public double Latitude { get; private set; }
        public double Longitude { get; private set; }
        public Task<string?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken)
        {
            Latitude = latitude;
            Longitude = longitude;
            return Task.FromResult(code);
        }
    }

    private sealed class FlakyRegionResolver : INuts2RegionResolver
    {
        public int Calls { get; private set; }
        public Task<string?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1) throw new HttpRequestException("Temporary GISCO outage.");
            return Task.FromResult<string?>("FR10");
        }
    }

    private sealed class StubTourismProvider(TourismStatistics? data = null) : ITourismStatisticsProvider
    {
        public bool Called { get; private set; }
        public Task<TourismStatistics?> GetAsync(string geographyCode, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(data);
        }
    }

    private sealed class FlakyTourismProvider(TourismStatistics data) : ITourismStatisticsProvider
    {
        public int Calls { get; private set; }
        public Task<TourismStatistics?> GetAsync(string geographyCode, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1) throw new HttpRequestException("Temporary outage.");
            return Task.FromResult<TourismStatistics?>(data);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(responseFactory(request));
        }
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
