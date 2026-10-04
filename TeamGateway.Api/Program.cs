using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Versioning;
using Serilog;
using TeamGateway.Api.Constants;
using TeamGateway.Api.Extensions;
using TeamGateway.Api.Middlewares;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGatewaySecurity(builder.Configuration, builder.Environment);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("gateway-session", policy => policy.RequireAuthenticatedUser());
});
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});
builder.Services.AddGatewayReverseProxy(builder.Configuration);
builder.Services.AddGatewayRateLimiter(builder.Configuration);
builder.Services.AddApiVersioningConfiguration();
builder.Services.AddCorsConfiguration(builder.Configuration);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services);
});

builder.Services.AddForwardedHeadersConfiguration(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} " +
        "in {Elapsed:0.0000} ms " +
        "IP={RemoteIpAddress} " +
        "XFF={XForwardedFor} " +
        "ClientActionId={ClientActionId} " +
        "ClientRequestId={ClientRequestId}";

    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set(
            "XForwardedFor",
            httpContext.Request.Headers["X-Forwarded-For"].ToString());

        diagnosticContext.Set(
            "RemoteIpAddress",
            httpContext.Connection.RemoteIpAddress?.ToString());

        diagnosticContext.Set(
            "ClientActionId",
            httpContext.Request.Headers["X-Client-Action-Id"].FirstOrDefault());

        diagnosticContext.Set(
            "ClientRequestId",
            httpContext.Request.Headers["X-Client-Request-Id"].FirstOrDefault());
    };
});
app.UseRouting();
app.UseCors(CorsPolicies.DefaultCors);
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<ClientCorrelationMiddleware>();
app.UseMiddleware<AntiforgeryValidationMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapReverseProxy().RequireRateLimiting(RateLimiterPolicies.Default);

app.Run();
