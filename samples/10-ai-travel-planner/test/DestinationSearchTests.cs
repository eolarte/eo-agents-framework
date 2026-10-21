using System.Net;
using System.Net.Sockets;
using System.Text;
using ai_travel_planner.Modules.Destinations.Api;
using ai_travel_planner.Modules.Destinations.Application;
using ai_travel_planner.Modules.Destinations.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ai_travel_planner.Tests;

public sealed class DestinationSearchTests
{
    [Fact]
    public async Task Search_maps_open_meteo_location_fields_and_stable_id()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {"results":[{"id":2988507,"name":"Paris","latitude":48.8566,"longitude":2.3522,"timezone":"Europe/Paris","country":"France","admin1":"Île-de-France"}]}
            """));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://geocoding-api.open-meteo.com/") };
        var client = new OpenMeteoGeocodingClient(httpClient);

        var results = await client.SearchAsync("Paris", CancellationToken.None);

        var destination = Assert.Single(results);
        Assert.Equal("open-meteo:2988507", destination.Id);
        Assert.Equal("Paris", destination.Name);
        Assert.Equal("France", destination.Country);
        Assert.Equal("Île-de-France", destination.Region);
        Assert.Equal(48.8566, destination.Latitude);
        Assert.Equal(2.3522, destination.Longitude);
        Assert.Equal("Europe/Paris", destination.Timezone);
        Assert.Contains("name=Paris", handler.LastRequestUri!.Query);
    }

    [Fact]
    public async Task Search_returns_empty_for_no_results()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse("{}"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://geocoding-api.open-meteo.com/") };
        var client = new OpenMeteoGeocodingClient(httpClient);

        var results = await client.SearchAsync("Nowhere", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Resolve_uses_provider_id_and_ignores_client_supplied_location()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {"id":2988507,"name":"Paris","latitude":48.8566,"longitude":2.3522,"timezone":"Europe/Paris","country":"France","admin1":"Île-de-France"}
            """));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://geocoding-api.open-meteo.com/") };
        var resolver = new OpenMeteoGeocodingClient(httpClient);

        var destination = await resolver.ResolveAsync("open-meteo:2988507", CancellationToken.None);

        Assert.NotNull(destination);
        Assert.Equal("open-meteo:2988507", destination.Id);
        Assert.Equal(48.8566, destination.Latitude);
        Assert.Equal("Europe/Paris", destination.Timezone);
        Assert.Equal("/v1/get?id=2988507", handler.LastRequestUri!.PathAndQuery);
    }

    [Theory]
    [InlineData("geo-paris")]
    [InlineData("open-meteo:-1")]
    [InlineData("open-meteo:0001")]
    [InlineData("open-meteo:123?latitude=0")]
    public async Task Resolve_rejects_invalid_id_without_provider_call(string id)
    {
        using var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("Provider should not be called."));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://geocoding-api.open-meteo.com/") };
        var resolver = new OpenMeteoGeocodingClient(httpClient);

        Assert.Null(await resolver.ResolveAsync(id, CancellationToken.None));
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task Resolve_rejects_provider_response_with_different_id()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {"id":42,"name":"Other","latitude":48.8566,"longitude":2.3522,"timezone":"Europe/Paris"}
            """));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://geocoding-api.open-meteo.com/") };
        var resolver = new OpenMeteoGeocodingClient(httpClient);

        Assert.Null(await resolver.ResolveAsync("open-meteo:2988507", CancellationToken.None));
    }

    [Fact]
    public async Task Resolve_rejects_missing_coordinates()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {"id":2988507,"name":"Paris","timezone":"Europe/Paris"}
            """));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://geocoding-api.open-meteo.com/") };
        var resolver = new OpenMeteoGeocodingClient(httpClient);

        Assert.Null(await resolver.ResolveAsync("open-meteo:2988507", CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    public async Task Api_rejects_missing_or_short_query(string? query)
    {
        await using var app = await StartAppAsync(_ => JsonResponse("{}"));
        using var client = CreateClient(app);

        var response = await client.GetAsync("/api/destinations/search" + (query is null ? "" : $"?q={Uri.EscapeDataString(query)}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Api_returns_gateway_error_when_geocoding_provider_fails()
    {
        await using var app = await StartAppAsync(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = CreateClient(app);

        var response = await client.GetAsync("/api/destinations/search?q=Paris");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("temporarily unavailable", body);
    }

    private static async Task<WebApplication> StartAppAsync(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddHttpClient<IDestinationSearchService, OpenMeteoGeocodingClient>(client =>
            client.BaseAddress = new Uri("https://geocoding-api.open-meteo.com/"))
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(responseFactory));

        var app = builder.Build();
        app.MapDestinationsModule();
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new HttpClient { BaseAddress = new Uri(address) };
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(responseFactory(request));
        }
    }
}
