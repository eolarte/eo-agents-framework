using System.IO.Pipelines;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace food_ordering.Infrastructure;

public sealed class RestaurantMcpConnection : IAsyncDisposable
{
    private readonly McpServer server;
    private readonly McpClient client;

    private RestaurantMcpConnection(McpServer server, McpClient client, IReadOnlyList<AITool> tools)
    { this.server = server; this.client = client; Tools = tools; }

    public IReadOnlyList<AITool> Tools { get; }

    public static async Task<RestaurantMcpConnection> CreateAsync()
    {
        // The in-memory stream transport exercises MCP discovery without a separate process.
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var server = McpServer.Create(
            new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()),
            new McpServerOptions
            {
                ToolCollection = [McpServerTool.Create((string question) => RestaurantMcpTools.GetRestaurantInformation(question),
                    new McpServerToolCreateOptions
                    { Name = "get_restaurant_information", Description = "Looks up restaurant hours, delivery zones, and estimated delivery times." })]
            });
        _ = server.RunAsync();
        var client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));
        return new RestaurantMcpConnection(server, client, (await client.ListToolsAsync()).Cast<AITool>().ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
        await server.DisposeAsync();
    }
}
