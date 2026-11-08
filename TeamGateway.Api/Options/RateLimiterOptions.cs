namespace TeamGateway.Api.Options;

public sealed class RateLimiterOptions
{
    public const string SectionName = "RateLimiter";

    public AuthRateLimiterOptions Auth { get; init; } = new();
    public StandardRateLimiterOptions Standard { get; init; } = new();
    public ExportRateLimiterOptions Export { get; set; } = new();
}

public sealed class AuthRateLimiterOptions
{
    public int PermitLimit { get; init; }
    public TimeSpan Window { get; init; }
    public int SegmentsPerWindow { get; init; }
    public int QueueLimit { get; init; }
}

public sealed class StandardRateLimiterOptions
{
    public int PermitLimit { get; init; }
    public TimeSpan Window { get; init; }
    public int SegmentsPerWindow { get; init; }
    public int QueueLimit { get; init; }
}


public sealed class ExportRateLimiterOptions
{
    public int PermitLimit { get; init; }
    public TimeSpan Window { get; init; }
    public int SegmentsPerWindow { get; init; }
    public int QueueLimit { get; init; }
}