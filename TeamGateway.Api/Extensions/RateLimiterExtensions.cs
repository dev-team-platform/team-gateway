using System.Threading.RateLimiting;
using TeamGateway.Api.Constants;
using TeamGateway.Api.Options;

namespace TeamGateway.Api.Extensions;

public static class RateLimiterExtensions
{
    public static IServiceCollection AddGatewayRateLimiter(
    this IServiceCollection services,
    IConfiguration configuration)
    {
        services
            .AddOptions<RateLimiterOptions>()
            .BindConfiguration(RateLimiterOptions.SectionName)
            .Validate(x => x.Auth.PermitLimit > 0, "RateLimiter:Auth:PermitLimit must be positive.")
            .Validate(x => x.Auth.Window > TimeSpan.Zero, "RateLimiter:Auth:Window must be positive.")
            .Validate(x => x.Auth.SegmentsPerWindow > 0, "RateLimiter:Auth:SegmentsPerWindow must be positive.")
            .Validate(x => x.Auth.QueueLimit >= 0, "RateLimiter:Auth:QueueLimit cannot be negative.")
            .Validate(x => x.Default.TokenLimit > 0, "RateLimiter:Default:TokenLimit must be positive.")
            .Validate(x => x.Default.TokensPerPeriod > 0, "RateLimiter:Default:TokensPerPeriod must be positive.")
            .Validate(x => x.Default.ReplenishmentPeriod > TimeSpan.Zero, "RateLimiter:Default:ReplenishmentPeriod must be positive.")
            .Validate(x => x.Default.QueueLimit >= 0, "RateLimiter:Default:QueueLimit cannot be negative.")
            .ValidateOnStart();

        var options = configuration
            .GetRequiredSection(RateLimiterOptions.SectionName)
            .Get<RateLimiterOptions>()!;

        services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiterOptions.AddPolicy(RateLimiterPolicies.Default, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    GetClientIpAddress(context),
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = options.Default.TokenLimit,
                        TokensPerPeriod = options.Default.TokensPerPeriod,
                        ReplenishmentPeriod = options.Default.ReplenishmentPeriod,
                        QueueLimit = options.Default.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            rateLimiterOptions.AddPolicy(RateLimiterPolicies.Auth, context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    GetClientIpAddress(context),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = options.Auth.PermitLimit,
                        Window = options.Auth.Window,
                        SegmentsPerWindow = options.Auth.SegmentsPerWindow,
                        QueueLimit = options.Auth.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

    private static string GetClientIpAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
