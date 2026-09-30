namespace food_ordering.Infrastructure;

public sealed record FoodOrderingConfiguration(
    string ApiKey,
    string ModelName,
    string EmbeddingModelName,
    bool OtelEnabled,
    string OrderDatabasePath,
    int SimulationMinDelaySeconds,
    int SimulationMaxDelaySeconds)
{
    public static FoodOrderingConfiguration Load()
    {
        var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");

        var databasePath = Environment.GetEnvironmentVariable("FOOD_ORDERING_DATABASE_PATH");
        if (string.IsNullOrWhiteSpace(databasePath))
            databasePath = Path.Combine(CustomerMemory.GetRoot(), "food-ordering-orders.db");

        var minimumDelay = ReadDelay("FOOD_ORDERING_SIMULATION_MIN_DELAY_SECONDS", 10);
        var maximumDelay = ReadDelay("FOOD_ORDERING_SIMULATION_MAX_DELAY_SECONDS", 30);
        if (minimumDelay > maximumDelay)
            throw new InvalidOperationException("FOOD_ORDERING_SIMULATION_MIN_DELAY_SECONDS must not exceed FOOD_ORDERING_SIMULATION_MAX_DELAY_SECONDS.");

        return new FoodOrderingConfiguration(
            apiKey,
            Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini",
            Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL") ?? "text-embedding-3-small",
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")),
            Path.GetFullPath(databasePath),
            minimumDelay,
            maximumDelay);
    }

    private static int ReadDelay(string variable, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        if (!int.TryParse(value, out var seconds) || seconds is < 1 or > 3600)
            throw new InvalidOperationException($"{variable} must be a whole number between 1 and 3600.");
        return seconds;
    }
}

public sealed class CustomerMemory(string root)
{
    public string Root { get; } = root;

    public static string GetRoot()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
            throw new InvalidOperationException("Could not determine the local application-data directory for customer memory.");
        return Path.Combine(localApplicationData, "Microsoft", "AgentFramework", "food-ordering-memory");
    }
}
