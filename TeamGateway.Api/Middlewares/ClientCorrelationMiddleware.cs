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
}