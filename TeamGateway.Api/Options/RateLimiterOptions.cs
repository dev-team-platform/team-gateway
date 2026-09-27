namespace TeamGateway.Api.Options;

public sealed class RateLimiterOptions
{
    public const string SectionName = "RateLimiter";

    public AuthRateLimiterOptions Auth { get; init; } = new();
    public DefaultRateLimiterOptions Default { get; init; } = new();
}

public sealed class AuthRateLimiterOptions
{
    public int PermitLimit { get; init; }
    public TimeSpan Window { get; init; }
    public int SegmentsPerWindow { get; init; }
    public int QueueLimit { get; init; }
}

public sealed class DefaultRateLimiterOptions
{
    public int TokenLimit { get; init; }
    public int TokensPerPeriod { get; init; }
    public TimeSpan ReplenishmentPeriod { get; init; }
    public int QueueLimit { get; init; }
}
