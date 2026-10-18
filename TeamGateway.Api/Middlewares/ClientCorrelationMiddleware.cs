using TeamGateway.Api.Attributes;
using TeamGateway.Api.Constants;

namespace TeamGateway.Api.Middlewares;

public sealed class ClientCorrelationMiddleware
{
    private readonly RequestDelegate _next;

    public ClientCorrelationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();

        // Browser WebSocket APIs do not allow callers to set arbitrary request headers.
        // SignalR can send these headers during HTTP negotiation, but not on the actual
        // WebSocket upgrade request, so don't reject a handshake for missing correlation IDs.
        if (IsWebSocketUpgradeRequest(context))
        {
            await _next(context);
            return;
        }

        // Endpoint explicitly skips validation
        if (endpoint?.Metadata.GetMetadata<SkipClientCorrelationAttribute>() is not null)
        {
            await _next(context);
            return;
        }

        var actionId = context.Request.Headers[ClientCorrelationHeaders.ClientActionId]
                .FirstOrDefault();

        var requestId = context.Request.Headers[ClientCorrelationHeaders.ClientRequestId]
                .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(actionId))
        {
            await WriteBadRequestAsync(context, ClientCorrelationHeaders.ClientActionId);
            return;
        }

        if (string.IsNullOrWhiteSpace(requestId))
        {
            await WriteBadRequestAsync(context, ClientCorrelationHeaders.ClientRequestId);
            return;
        }

        if (!Guid.TryParse(actionId, out _))
        {
            await WriteInvalidHeaderAsync(context, ClientCorrelationHeaders.ClientActionId);
            return;
        }

        if (!Guid.TryParse(requestId, out _))
        {
            await WriteInvalidHeaderAsync(context, ClientCorrelationHeaders.ClientRequestId);
            return;
        }

        await _next(context);
    }

    private static async Task WriteBadRequestAsync(HttpContext context, string header)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        await context.Response.WriteAsJsonAsync(new
        {
            message = "Missing required header",
            details = $"Header '{header}' is required."
        });
    }

    private static async Task WriteInvalidHeaderAsync(HttpContext context, string header)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        await context.Response.WriteAsJsonAsync(new
        {
            message = "Invalid header",
            details = $"Header '{header}' must be a valid UUID."
        });
    }

    private static bool IsWebSocketUpgradeRequest(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            return true;
        }

        // YARP can proxy an upgrade without this app installing WebSocket middleware,
        // so inspect the HTTP/1.1 handshake headers as well.
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            return false;
        }

        var upgradeHeader = context.Request.Headers["Upgrade"].ToString();
        var connectionHeader = context.Request.Headers["Connection"].ToString();

        return string.Equals(upgradeHeader, "websocket", StringComparison.OrdinalIgnoreCase)
            && connectionHeader.Split(',').Any(value =>
                string.Equals(value.Trim(), "upgrade", StringComparison.OrdinalIgnoreCase));
    }
}
