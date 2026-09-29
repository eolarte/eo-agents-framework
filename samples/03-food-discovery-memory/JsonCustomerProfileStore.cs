using System.Text;
using System.Text.Json;

sealed class JsonCustomerProfileStore
{
    private readonly string directoryPath;

    public JsonCustomerProfileStore()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "Could not determine the local application-data directory for customer profiles.");
        }

        directoryPath = Path.Combine(
            localApplicationData,
            "Microsoft",
            "AgentFramework",
            "food-discovery-memory",
            "users");
    }

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true
    };

    public async Task<CustomerMemoryState?> LoadAsync(
        string customerName,
        CancellationToken cancellationToken)
    {
        var path = GetProfilePath(customerName);

        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var profile = await JsonSerializer.DeserializeAsync<CustomerMemoryState>(
            stream,
            JsonOptions,
            cancellationToken);

        if (profile is null || string.IsNullOrWhiteSpace(profile.CustomerName))
        {
            throw new InvalidDataException(
                $"The customer profile at '{path}' is incomplete.");
        }

        if (!string.Equals(
                CreateSafeFileStem(profile.CustomerName),
                CreateSafeFileStem(customerName),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The customer profile at '{path}' does not match the requested customer.");
        }

        return profile;
    }

    public async Task SaveAsync(
        CustomerMemoryState profile,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.CustomerName))
        {
            throw new InvalidOperationException(
                "Cannot save a customer profile without a customer name.");
        }

        Directory.CreateDirectory(directoryPath);
        var path = GetProfilePath(profile.CustomerName);
        var temporaryPath = $"{path}.{Environment.ProcessId}.tmp";
        var json = JsonSerializer.Serialize(profile, JsonOptions);

        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string GetProfilePath(string customerName)
    {
        var fileStem = CreateSafeFileStem(customerName);
        return Path.Combine(directoryPath, $"{fileStem}.json");
    }

    private static string CreateSafeFileStem(string customerName)
    {
        var builder = new StringBuilder();
        var separatorPending = false;

        foreach (var character in customerName.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (separatorPending && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(character);
                separatorPending = false;
            }
            else
            {
                separatorPending = true;
            }
        }

        if (builder.Length == 0)
        {
            throw new ArgumentException(
                "Customer name must contain at least one letter or digit.",
                nameof(customerName));
        }

        return builder.ToString();
    }
}
