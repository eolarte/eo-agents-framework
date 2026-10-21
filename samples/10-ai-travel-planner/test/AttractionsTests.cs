using System.Net;
using System.Net.Sockets;
using System.Text;
using ai_travel_planner.Modules.Attractions.Api;
using ai_travel_planner.Modules.Attractions.Application;
using ai_travel_planner.Modules.Attractions.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ai_travel_planner.Tests;

public sealed class AttractionsTests
{
    [Fact]
    public async Task Api_returns_normalized_ranked_places_and_uses_minimal_field_mask()
    {
        using var app = await StartAppAsync(_ => JsonResponse("""
            {"places":[
              {"id":"p2","displayName":{"text":"Far Museum"},"location":{"latitude":48.86,"longitude":2.36},"types":["museum"],"primaryType":"museum"},
              {"id":"p1","displayName":{"text":"Near Museum"},"location":{"latitude":48.8567,"longitude":2.3523},"types":["museum","point_of_interest"],"primaryType":"museum"},
              {"id":"p1","displayName":{"text":"Duplicate ID"},"location":{"latitude":48.8566,"longitude":2.3522},"types":["museum"]}
            ]}
            """));
        using var client = CreateClient(app);

        var response = await client.GetAsync("/api/attractions?destinationId=open-meteo%3A1&latitude=48.8566&longitude=2.3522&interests=museum");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("google-places:p1", body);
        Assert.Contains("Near Museum", body);
        Assert.True(body.IndexOf("Near Museum", StringComparison.Ordinal) < body.IndexOf("Far Museum", StringComparison.Ordinal));
        var handler = app.Services.GetRequiredService<RecordingHandler>();
        Assert.Equal("places.id,places.displayName,places.location,places.types,places.primaryType", handler.FieldMask);
        Assert.Equal("test-api-key", handler.ApiKey);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(new Uri("https://places.googleapis.com/v1/places:searchNearby"), handler.RequestUri);
        Assert.Contains("\"includedTypes\":[\"museum\",\"art_museum\",\"history_museum\"]", handler.RequestBody);
        Assert.Contains("\"radius\":5000", handler.RequestBody);
        Assert.Contains("\"maxResultCount\":20", handler.RequestBody);
    }

    [Fact]
    public async Task Api_defaults_to_visitor_types_and_drops_non_attractions_from_mixed_places_response()
    {
        using var app = await StartAppAsync(_ => JsonResponse("""
            {"places":[
              {"id":"sight","displayName":{"text":"Notre Dame"},"location":{"latitude":48.8532,"longitude":2.3491},"types":["historical_landmark","point_of_interest"]},
              {"id":"sculpture","displayName":{"text":"Charlemagne Statue"},"location":{"latitude":48.8531,"longitude":2.3482},"types":["sculpture"]},
              {"id":"office","displayName":{"text":"FLOHYMONT"},"location":{"latitude":48.8536,"longitude":2.3496},"types":["corporate_office","point_of_interest"]},
              {"id":"toilet","displayName":{"text":"Public Toilets"},"location":{"latitude":48.8538,"longitude":2.3492},"types":["public_bathroom","point_of_interest"]},
              {"id":"hotel","displayName":{"text":"Nearby Hotel"},"location":{"latitude":48.8539,"longitude":2.3491},"types":["lodging","point_of_interest"]},
              {"id":"hospital","displayName":{"text":"Nearby Hospital"},"location":{"latitude":48.8539,"longitude":2.3492},"types":["hospital","point_of_interest"]},
              {"id":"tour","displayName":{"text":"Walking Tour Office"},"location":{"latitude":48.8531,"longitude":2.3481},"types":["tour_agency","point_of_interest"]},
              {"id":"generic","displayName":{"text":"Unclassified Place"},"location":{"latitude":48.8530,"longitude":2.3480},"types":["point_of_interest","establishment"]}
            ]}
            """));
        using var client = CreateClient(app);

        var response = await client.GetAsync("/api/attractions?destinationId=paris&latitude=48.8532&longitude=2.3491");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Notre Dame", body);
        Assert.Contains("Charlemagne Statue", body);
        Assert.DoesNotContain("FLOHYMONT", body);
        Assert.DoesNotContain("Public Toilets", body);
        Assert.DoesNotContain("Nearby Hotel", body);
        Assert.DoesNotContain("Nearby Hospital", body);
        Assert.DoesNotContain("Walking Tour Office", body);
        Assert.DoesNotContain("Unclassified Place", body);
        var handler = app.Services.GetRequiredService<RecordingHandler>();
        Assert.Contains("\"includedTypes\":[\"tourist_attraction\",\"historical_landmark\",\"historical_place\",\"cultural_landmark\",\"museum\",\"art_gallery\",\"monument\",\"sculpture\",\"plaza\",\"park\"]", handler.RequestBody);

        var unknownInterestResponse = await client.GetAsync("/api/attractions?destinationId=paris&latitude=48.8532&longitude=2.3491&interests=unmapped");
        Assert.Equal(HttpStatusCode.OK, unknownInterestResponse.StatusCode);
        Assert.Contains("\"includedTypes\":[\"tourist_attraction\",\"historical_landmark\",\"historical_place\",\"cultural_landmark\",\"museum\",\"art_gallery\",\"monument\",\"sculpture\",\"plaza\",\"park\"]", handler.RequestBody);
    }

    [Fact]
    public async Task Api_uses_mapped_interest_types_and_adds_food_or_shopping_only_when_requested()
    {
        await using var app = await StartAppAsync(_ => JsonResponse("{}"));
        using var client = CreateClient(app);

        var historyResponse = await client.GetAsync("/api/attractions?destinationId=paris&latitude=48.85&longitude=2.35&interests=history,art");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var handler = app.Services.GetRequiredService<RecordingHandler>();
        Assert.Contains("historical_landmark", handler.RequestBody);
        Assert.Contains("historical_place", handler.RequestBody);
        Assert.Contains("art_gallery", handler.RequestBody);
        Assert.Contains("museum", handler.RequestBody);
        Assert.DoesNotContain("restaurant", handler.RequestBody);
        Assert.DoesNotContain("shopping_mall", handler.RequestBody);

        var foodResponse = await client.GetAsync("/api/attractions?destinationId=paris&latitude=48.85&longitude=2.35&interests=food,shopping");
        Assert.Equal(HttpStatusCode.OK, foodResponse.StatusCode);
        Assert.Contains("restaurant", handler.RequestBody);
        Assert.Contains("shopping_mall", handler.RequestBody);
        Assert.Contains("farmers_market", handler.RequestBody);
    }

    [Fact]
    public async Task Api_returns_503_when_places_key_is_missing()
    {
        await using var app = await StartAppAsync(_ => JsonResponse("{}"), includeApiKey: false);
        using var client = CreateClient(app);

        var response = await client.GetAsync("/api/attractions?destinationId=dest&latitude=1&longitude=2");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("GOOGLE_PLACES_API_KEY", body);
    }

    [Theory]
    [InlineData("?destinationId=x&latitude=91&longitude=2")]
    [InlineData("?destinationId=x&latitude=1&longitude=181")]
    [InlineData("?destinationId=x&latitude=NaN&longitude=2")]
    [InlineData("?destinationId=x&latitude=1&longitude=2&interests=a,b,c,d,e,f,g,h,i,j,k,l,m")]
    public async Task Api_rejects_invalid_or_unbounded_input(string query)
    {
        await using var app = await StartAppAsync(_ => JsonResponse("{}"));
        using var client = CreateClient(app);

        var response = await client.GetAsync("/api/attractions" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Api_returns_502_when_places_provider_fails()
    {
        await using var app = await StartAppAsync(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = CreateClient(app);

        var response = await client.GetAsync("/api/attractions?destinationId=x&latitude=1&longitude=2");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Api_returns_502_when_places_provider_times_out()
    {
        await using var app = await StartAppAsync(_ => throw new TaskCanceledException("Provider request timed out."));
        using var client = CreateClient(app);

        var response = await client.GetAsync("/api/attractions?destinationId=x&latitude=1&longitude=2");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("request timed out", body);
    }

    private static async Task<WebApplication> StartAppAsync(Func<HttpRequestMessage, HttpResponseMessage> responseFactory, bool includeApiKey = true)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Configuration["GOOGLE_PLACES_API_KEY"] = includeApiKey ? "test-api-key" : string.Empty;
        builder.Services.AddSingleton(new RecordingHandler(responseFactory));
        builder.Services.AddHttpClient<IAttractionsProvider, GooglePlacesAttractionsProvider>(client =>
            client.BaseAddress = new Uri("https://places.googleapis.com/v1/"))
            .ConfigurePrimaryHttpMessageHandler(provider => provider.GetRequiredService<RecordingHandler>());

        var app = builder.Build();
        app.MapAttractionsModule();
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

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public string? FieldMask { get; private set; }
        public string? ApiKey { get; private set; }
        public string? RequestBody { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            FieldMask = request.Headers.GetValues("X-Goog-FieldMask").Single();
            ApiKey = request.Headers.GetValues("X-Goog-Api-Key").Single();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}
