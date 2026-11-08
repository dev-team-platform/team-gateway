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
            .Validate(x => x.Standard.PermitLimit > 0, "RateLimiter:Standard:PermitLimit must be positive.")
            .Validate(x => x.Standard.Window > TimeSpan.Zero, "RateLimiter:Standard:Window must be positive.")
            .Validate(x => x.Standard.SegmentsPerWindow > 0, "RateLimiter:Standard:SegmentsPerWindow must be positive.")
            .Validate(x => x.Standard.SegmentsPerWindow > 0 && x.Standard.Window.Ticks >= x.Standard.SegmentsPerWindow,
                "RateLimiter:Standard:Window must be at least one tick per segment.")
            .Validate(x => x.Standard.QueueLimit >= 0, "RateLimiter:Standard:QueueLimit cannot be negative.")
            .Validate(x => x.Export.PermitLimit > 0, "RateLimiter:Export:PermitLimit must be positive.")
            .Validate(x => x.Export.Window > TimeSpan.Zero, "RateLimiter:Export:Window must be positive.")
            .Validate(x => x.Export.SegmentsPerWindow > 0, "RateLimiter:Export:SegmentsPerWindow must be positive.")
            .Validate(x => x.Export.SegmentsPerWindow > 0 && x.Export.Window.Ticks >= x.Export.SegmentsPerWindow,
                "RateLimiter:Export:Window must be at least one tick per segment.")
            .Validate(x => x.Export.QueueLimit >= 0, "RateLimiter:Export:QueueLimit cannot be negative.")
            .ValidateOnStart();

        var options = configuration
            .GetRequiredSection(RateLimiterOptions.SectionName)
            .Get<RateLimiterOptions>()!;

        services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiterOptions.AddPolicy(RateLimiterPolicies.Standard, context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    GetIdentityPartition(context),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = options.Standard.PermitLimit,
                        Window = options.Standard.Window,
                        SegmentsPerWindow = options.Standard.SegmentsPerWindow,
                        QueueLimit = options.Standard.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            rateLimiterOptions.AddPolicy(RateLimiterPolicies.Export, context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    GetIdentityPartition(context),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = options.Export.PermitLimit,
                        Window = options.Export.Window,
                        SegmentsPerWindow = options.Export.SegmentsPerWindow,
                        QueueLimit = options.Export.QueueLimit,
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

    private static string GetIdentityPartition(HttpContext context)
    {
        var subject = context.User.Identities
            .Where(identity => identity.IsAuthenticated)
            .Select(identity => identity.FindFirst("sub")?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return subject is null ? "anonymous" : $"subject:{subject}";
    }

    private static string GetClientIpAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
