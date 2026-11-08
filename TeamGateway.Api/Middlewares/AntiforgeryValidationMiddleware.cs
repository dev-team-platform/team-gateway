using Microsoft.AspNetCore.Antiforgery;

namespace TeamGateway.Api.Middlewares;

public sealed class AntiforgeryValidationMiddleware
{
    private readonly RequestDelegate _next;

    public AntiforgeryValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (IsSafeMethod(context.Request.Method))
        {
            await _next(context);
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
            await _next(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "The CSRF token is missing or invalid."
            });
        }
    }

    private static bool IsSafeMethod(string method) => HttpMethods.IsGet(method)
        || HttpMethods.IsHead(method)
        || HttpMethods.IsOptions(method)
        || HttpMethods.IsTrace(method);
}
