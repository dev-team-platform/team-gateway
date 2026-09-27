namespace TeamGateway.Api.Options;

public class AuthOptions
{
    public const string SectionName = "Auth";
    public List<string> AllowRedirectOrigins { get; init; } = [];
    public AuthCookieOptions Cookie { get; init; } = null!;
    public KeycloakOptions Keycloak { get; init; } = null!;
    public InternalJwtOptions InternalJwt { get; init; } = null!;
}

public sealed class AuthCookieOptions
{
    public string CookieName { get; init; } = null!;
    public bool HttpOnly { get; init; }
    public string SameSite { get; init; } = null!;
    public string SecurePolicy { get; init; } = null!;
    public string Path { get; init; } = "/";
    public TimeSpan ExpireTimeSpan { get; init; }
    public bool SlidingExpiration { get; init; }
}

public sealed class KeycloakOptions
{
    public string Authority { get; init; } = null!;
    public string ClientId { get; init; } = null!;
    public string ClientSecret { get; init; } = null!;
    public string CallbackPath { get; init; } = null!;
    public string SignedOutCallbackPath { get; init; } = null!;
    public string ChangePasswordAction { get; init; } = null!;
    public TimeSpan RefreshBeforeExpiry { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan RefreshLockTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed class InternalJwtOptions
{
    public string Issuer { get; init; } = null!;
    public Dictionary<string, string> AudienceMappings { get; init; } = new();
    public string PrivateKeyPemPath { get; init; } = null!;
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(5);
}
