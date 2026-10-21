using System.Net;
using System.Text;
using ai_travel_planner.Modules.Weather.Api;
using ai_travel_planner.Modules.Weather.Application;
using ai_travel_planner.Modules.Weather.Domain;
using ai_travel_planner.Modules.Weather.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ai_travel_planner.Tests;

public sealed class WeatherTests
{
    private static readonly WeatherLocation Location = new(48.85, 2.35, "Europe/Paris");

    [Fact]
    public async Task Assessment_splits_forecast_and_historical_days_with_provenance()
    {
        var provider = new FixtureWeatherProvider(
            [new WeatherSample(new DateOnly(2026, 5, 15), 30, 18, 2, 1)],
            Enumerable.Range(2016, 10).Select(year =>
                new WeatherSample(new DateOnly(year, 5, 20), year - 1996, 10, year - 2010, 2)).ToArray());
        var service = CreateService(provider, new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));

        var result = await service.GetAsync("geo:paris", Location, new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 20), CancellationToken.None);

        Assert.Equal(2, result.Periods.Count);
        Assert.Equal(WeatherBasis.Forecast, result.Periods[0].Basis);
        Assert.Equal(WeatherBasis.Historical, result.Periods[1].Basis);
        Assert.Equal("https://api.open-meteo.com/v1/forecast", result.Periods[0].ProviderEndpoint);
        Assert.Equal("https://archive-api.open-meteo.com/v1/archive", result.Periods[1].ProviderEndpoint);
        var historical = result.Days.Single(day => day.Date == new DateOnly(2026, 5, 20));
        Assert.Equal(10, historical.SampleYears);
        Assert.Equal(24.5, historical.TemperatureMaxC);
        Assert.Equal("Open-Meteo", result.Provider);
        Assert.Equal("°C", result.TemperatureUnit);
        Assert.Equal(1, provider.ArchiveCallCount);
    }

    [Fact]
    public async Task Forecast_horizon_uses_destination_local_date()
    {
        var provider = new FixtureWeatherProvider(
            [new WeatherSample(new DateOnly(2026, 5, 17), 22, 12, 0, 1)], []);
        var service = CreateService(provider, new DateTimeOffset(2026, 5, 1, 23, 30, 0, TimeSpan.Zero));

        var result = await service.GetAsync("geo:paris", Location, new DateOnly(2026, 5, 17), new DateOnly(2026, 5, 17), CancellationToken.None);

        Assert.Equal(WeatherBasis.Forecast, Assert.Single(result.Days).Basis);
        Assert.DoesNotContain(result.Periods, period => period.Basis == WeatherBasis.Historical);
        Assert.Equal(0, provider.ArchiveCallCount);
    }

    [Fact]
    public async Task Historical_february_29_uses_interpolated_non_leap_samples()
    {
        var archive = Enumerable.Range(2016, 10).SelectMany(year => DateTime.IsLeapYear(year)
            ? new[] { new WeatherSample(new DateOnly(year, 2, 29), 12, 2, 4, 1) }
            : new[]
            {
                new WeatherSample(new DateOnly(year, 2, 28), 10, 0, 2, 1),
                new WeatherSample(new DateOnly(year, 3, 1), 14, 4, 6, 2)
            }).ToArray();
        var service = CreateService(new FixtureWeatherProvider([], archive), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var result = await service.GetAsync("geo:paris", Location, new DateOnly(2028, 2, 29), new DateOnly(2028, 2, 29), CancellationToken.None);

        var day = Assert.Single(result.Days);
        Assert.Equal(12, day.TemperatureMaxC);
        Assert.Equal(4, day.PrecipitationMm);
        Assert.Contains(day.Warnings, warning => warning.Contains("non-leap", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(10, day.SampleYears);
    }

    [Theory]
    [InlineData(15)]
    public async Task Assessment_rejects_invalid_inclusive_trip_length(int extraDays)
    {
        var service = CreateService(new FixtureWeatherProvider([], []), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var end = new DateOnly(2026, 1, 1).AddDays(extraDays);

        await Assert.ThrowsAsync<WeatherRequestException>(() => service.GetAsync(
            "geo:paris", Location, new DateOnly(2026, 1, 1), end, CancellationToken.None));
    }

    [Fact]
    public async Task Open_meteo_provider_maps_daily_arrays_and_reports_provider_failure()
    {
        using var successHandler = new StubHttpMessageHandler(_ => JsonResponse("""
            {"daily":{"time":["2026-05-15"],"temperature_2m_max":[24],"temperature_2m_min":[12],"precipitation_sum":[0.5],"weather_code":[3]}}
            """));
        using var successClient = new HttpClient(successHandler) { BaseAddress = new Uri("https://api.open-meteo.com/") };
        var provider = new OpenMeteoWeatherProvider(successClient);

        var sample = Assert.Single(await provider.GetForecastAsync(Location, new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 15), CancellationToken.None));
        Assert.Equal(24, sample.TemperatureMaxC);
        Assert.Equal("https://api.open-meteo.com/v1/forecast", successHandler.LastRequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Contains("timezone=Europe%2FParis", successHandler.LastRequestUri!.Query);
        Assert.DoesNotContain("forecast_days=", successHandler.LastRequestUri.Query);

        using var failureHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var failureClient = new HttpClient(failureHandler) { BaseAddress = new Uri("https://api.open-meteo.com/") };
        var failureProvider = new OpenMeteoWeatherProvider(failureClient);
        await Assert.ThrowsAsync<WeatherProviderException>(() => failureProvider.GetArchiveAsync(
            Location, new DateOnly(2016, 1, 1), new DateOnly(2025, 12, 31), CancellationToken.None));
        Assert.Equal("https://archive-api.open-meteo.com/v1/archive", failureHandler.LastRequestUri!.GetLeftPart(UriPartial.Path));
        Assert.DoesNotContain("forecast_days=", failureHandler.LastRequestUri!.Query);

        using var timeoutHandler = new StubHttpMessageHandler(_ => throw new TaskCanceledException("Simulated HTTP timeout."));
        using var timeoutClient = new HttpClient(timeoutHandler) { BaseAddress = new Uri("https://api.open-meteo.com/") };
        var timeoutProvider = new OpenMeteoWeatherProvider(timeoutClient);
        var timeout = await Assert.ThrowsAsync<WeatherProviderException>(() => timeoutProvider.GetForecastAsync(
            Location, new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 15), CancellationToken.None));
        Assert.IsType<TaskCanceledException>(timeout.InnerException);

        using var canceledRequest = new CancellationTokenSource();
        canceledRequest.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => timeoutProvider.GetForecastAsync(
            Location, new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 15), canceledRequest.Token));
    }

    [Fact]
    public async Task Api_returns_400_for_invalid_input_and_502_when_provider_fails()
    {
        await using var app = await StartAppAsync(new FailingWeatherProvider());
        using var client = CreateClient(app);
        const string validQuery = "destinationId=geo%3Aparis&latitude=48.85&longitude=2.35&timezone=Europe%2FParis&startDate=2026-05-15&endDate=2026-05-15";

        var invalid = await client.GetAsync("/api/weather?" + validQuery.Replace("latitude=48.85", "latitude=200", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var failedProvider = await client.GetAsync("/api/weather?" + validQuery);
        Assert.Equal(HttpStatusCode.BadGateway, failedProvider.StatusCode);
    }

    private static WeatherAssessmentService CreateService(IWeatherProvider provider, DateTimeOffset now) =>
        new(provider, new FixedTimeProvider(now));

    private static async Task<WebApplication> StartAppAsync(IWeatherProvider provider)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddWeatherModule();
        builder.Services.AddSingleton(provider);
        builder.Services.AddSingleton<IWeatherProvider>(provider);
        builder.Services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero)));
        var app = builder.Build();
        app.MapWeatherModule();
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new HttpClient { BaseAddress = new Uri(address) };
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class FixtureWeatherProvider(
        IReadOnlyList<WeatherSample> forecast, IReadOnlyList<WeatherSample> archive) : IWeatherProvider
    {
        public int ArchiveCallCount { get; private set; }
        public Task<IReadOnlyList<WeatherSample>> GetForecastAsync(WeatherLocation location, DateOnly start, DateOnly end, CancellationToken cancellationToken) => Task.FromResult(forecast);
        public Task<IReadOnlyList<WeatherSample>> GetArchiveAsync(WeatherLocation location, DateOnly start, DateOnly end, CancellationToken cancellationToken)
        {
            ArchiveCallCount++;
            return Task.FromResult(archive);
        }
    }

    private sealed class FailingWeatherProvider : IWeatherProvider
    {
        public Task<IReadOnlyList<WeatherSample>> GetForecastAsync(WeatherLocation location, DateOnly start, DateOnly end, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<WeatherSample>>(new WeatherProviderException("Fixture failure."));
        public Task<IReadOnlyList<WeatherSample>> GetArchiveAsync(WeatherLocation location, DateOnly start, DateOnly end, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<WeatherSample>>(new WeatherProviderException("Fixture failure."));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(responseFactory(request));
        }
    }
}
