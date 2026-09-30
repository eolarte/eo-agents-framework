using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace food_ordering.Web;

public static class CustomerChatEndpoints
{
    public static IEndpointRouteBuilder MapCustomerChat(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/chat", async (CustomerChatRequest request, CustomerChatService chat, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.CustomerId) || string.IsNullOrWhiteSpace(request.Message))
                return Results.BadRequest(new { error = "Both customerId and message are required." });

            var customerId = request.CustomerId.Trim();
            var result = await chat.SendAsync(customerId, request.Message.Trim(), cancellationToken);
            return Results.Ok(new { customerId, response = result.Response, orderStatus = result.OrderStatus });
        });
        return endpoints;
    }
}
