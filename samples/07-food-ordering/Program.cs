using CommunityToolkit.VectorData.InMemory;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Data.Sqlite;
using OpenAI;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var configuration = FoodOrderingConfiguration.Load();
var openAiClient = new OpenAIClient(configuration.ApiKey);
var chatClient = openAiClient.GetChatClient(configuration.ModelName)
    .AsIChatClient()
    .AsBuilder()
    .UseOpenTelemetry(sourceName: "FoodOrdering.ChatClient")
    .Build();
var embeddingGenerator = openAiClient.GetEmbeddingClient(configuration.EmbeddingModelName)
    .AsIEmbeddingGenerator(RestaurantKnowledge.EmbeddingDimensions);
var restaurant = new RestaurantData();
var vectorStore = new InMemoryVectorStore(new() { EmbeddingGenerator = embeddingGenerator });
var knowledge = new RestaurantKnowledge(vectorStore, embeddingGenerator, restaurant);
await knowledge.IndexAsync();
await using var mcpConnection = await RestaurantMcpConnection.CreateAsync();
var mcpTools = mcpConnection.Tools;
var memoryRoot = CustomerMemory.GetRoot();
var customerMemory = new CustomerMemory(memoryRoot);

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Services.AddSingleton(configuration);
var connectionString = new SqliteConnectionStringBuilder { DataSource = configuration.OrderDatabasePath }.ToString();
builder.Services.AddDbContextFactory<CustomerOrderDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddSingleton<IChatClient>(chatClient);
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingGenerator);
builder.Services.AddSingleton(restaurant);
builder.Services.AddSingleton<IRestaurantKnowledge>(knowledge);
builder.Services.AddSingleton<ICustomerOrderStore, CustomerOrderStore>();
builder.Services.AddSingleton<IReadOnlyList<AITool>>(mcpTools);
builder.Services.AddSingleton(customerMemory);
builder.Services.AddSingleton<ICustomerAgentFactory, FoodOrderingAgentFactory>();
builder.Services.AddSingleton(provider => provider.GetRequiredService<ICustomerAgentFactory>().Create("devui-demo"));
builder.Services.AddSingleton<CustomerChatService>();
builder.Services.AddHostedService<OrderLifecycleWorker>();

builder.AddAIAgent("FoodOrderingCoordinator", (services, _) => services.GetRequiredService<CustomerAgentSet>().Coordinator);
builder.AddAIAgent("MenuAgent", (services, _) => services.GetRequiredService<CustomerAgentSet>().MenuAgent);
builder.AddAIAgent("PolicyAgent", (services, _) => services.GetRequiredService<CustomerAgentSet>().PolicyAgent);
builder.AddAIAgent("CheckoutPaymentAgent", (services, _) => services.GetRequiredService<CustomerAgentSet>().CheckoutPaymentAgent);
builder.AddAIAgent("DeliveryAgent", (services, _) => services.GetRequiredService<CustomerAgentSet>().DeliveryAgent);
builder.AddAIAgent("OrderStatusAgent", (services, _) => services.GetRequiredService<CustomerAgentSet>().OrderStatusAgent);
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();
builder.Services.AddDevUI();

if (configuration.OtelEnabled)
{
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .AddSource("Experimental.Microsoft.Agents.AI")
            .AddSource("FoodOrdering.ChatClient")
            .AddSource("FoodOrdering.Sample")
            .AddOtlpExporter())
        .WithMetrics(metrics => metrics
            .AddMeter("Experimental.Microsoft.Agents.AI")
            .AddMeter("FoodOrdering.Sample")
            .AddOtlpExporter());
}

var app = builder.Build();
var databaseDirectory = Path.GetDirectoryName(configuration.OrderDatabasePath);
if (!string.IsNullOrEmpty(databaseDirectory)) Directory.CreateDirectory(databaseDirectory);
await using (var database = await app.Services.GetRequiredService<IDbContextFactory<CustomerOrderDbContext>>().CreateDbContextAsync())
{
    await database.Database.EnsureCreatedAsync();
}

app.UseStaticFiles();
app.MapOpenAIResponses();
app.MapOpenAIConversations();
if (app.Environment.IsDevelopment())
{
    app.MapDevUI();
}

app.MapGet("/", async () => Results.Content(
    await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "wwwroot", "customer-ui", "index.html")), "text/html"));
app.MapCustomerChat();

Console.WriteLine("Food ordering agent sample");
Console.WriteLine("Customer chat: /  |  DevUI: /devui (Development only)");
Console.WriteLine($"Customer memory folder: {memoryRoot}");
Console.WriteLine($"Local order database: {configuration.OrderDatabasePath}");
Console.WriteLine($"Indexed {restaurant.Menu.Count} menu items and {knowledge.PolicyDocumentCount} policy documents.");
Console.WriteLine(configuration.OtelEnabled
    ? "OpenTelemetry OTLP export is enabled."
    : "OpenTelemetry export is disabled. Set OTEL_EXPORTER_OTLP_ENDPOINT to send traces and metrics to Aspire Dashboard.");

await app.RunAsync();
