namespace TeamGateway.Api.Options;

public sealed class AntiforgeryOptions
{
    public const string SectionName = "Antiforgery";

    public string HeaderName { get; init; } = null!;
    public string CookieName { get; init; } = null!;
    public string RequestTokenCookieName { get; init; } = null!;
    public string SameSite { get; init; } = null!;
    public string SecurePolicy { get; init; } = null!;
    public string Path { get; init; } = "/";
}
