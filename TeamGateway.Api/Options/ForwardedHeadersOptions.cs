namespace TeamGateway.Api.Options;

public class ForwardedHeadersOptions
{
    public const string SectionName = "ForwardedHeaders";

    public string[] KnownProxies { get; init; } = [];
    public string[] KnownNetworks { get; init; } = [];
}