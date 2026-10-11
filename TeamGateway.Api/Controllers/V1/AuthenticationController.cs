using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using TeamGateway.Api.Attributes;
using TeamGateway.Api.Constants;
using TeamGateway.Api.Dtos.V1;
using TeamGateway.Api.Options;

namespace TeamGateway.Api.Controllers.V1;

[Route("api/v{version:apiVersion}/auth")]
[ApiVersion("1.0")]
[ApiController]
[EnableRateLimiting(RateLimiterPolicies.Auth)]
public class AuthenticationController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IAntiforgery _antiforgery;
    private readonly IOptions<Options.AntiforgeryOptions> _antiforgeryOptions;
    private readonly IOptionsMonitor<OpenIdConnectOptions> _openIdConnectOptions;
    private readonly IOptions<AuthOptions> _authOptions;
    private readonly IWebHostEnvironment _environment;

    public AuthenticationController(
        IConfiguration configuration,
        IAntiforgery antiforgery,
        IOptions<Options.AntiforgeryOptions> antiforgeryOptions,
        IOptionsMonitor<OpenIdConnectOptions> openIdConnectOptions,
        IOptions<AuthOptions> authOptions,
        IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _antiforgery = antiforgery;
        _antiforgeryOptions = antiforgeryOptions;
        _openIdConnectOptions = openIdConnectOptions;
        _authOptions = authOptions;
        _environment = environment;
    }

    [HttpGet("login")]
    [SkipClientCorrelation]
    public async Task<IActionResult> Login([FromQuery] string returnUrl)
    {
        var redirectUri = GetSafeReturnUrl(returnUrl);

        var bypassIdentity = _authOptions.Value.LocalBypassIdentity;
        if (_environment.IsDevelopment() && !string.IsNullOrWhiteSpace(bypassIdentity))
        {
            var identity = new ClaimsIdentity(
            [
                new Claim("sub", bypassIdentity)
            ], AuthenticationSchemes.ApplicationCookie, "preferred_username", "roles");

            var properties = new AuthenticationProperties();
            properties.Items[AuthenticationSchemes.LocalBypassProperty] = "true";

            await HttpContext.SignInAsync(
                AuthenticationSchemes.ApplicationCookie,
                new ClaimsPrincipal(identity),
                properties);

            return Redirect(redirectUri);
        }

        return Challenge(
            new AuthenticationProperties
            {
                RedirectUri = redirectUri
            },
            AuthenticationSchemes.Keycloak
        );
    }

    private string GetSafeReturnUrl(string returnUrl)
    {
        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Invalid returnUrl", nameof(returnUrl));
        }

        var allowedOrigins = _configuration
            .GetRequiredSection(AuthOptions.SectionName)
            .Get<AuthOptions>()!.AllowRedirectOrigins ?? [];

        var origin = $"{uri.Scheme}://{uri.Authority}";

        return allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)
            ? uri.ToString()
            : throw new ArgumentException("ReturnUrl is not allowed", nameof(returnUrl));
    }

    [HttpGet("access-denied")]
    [SkipClientCorrelation]
    public IActionResult AccessDenied()
    {
        return Forbid();
    }

    [Authorize]
    [HttpGet("csrf")]
    [EnableRateLimiting(RateLimiterPolicies.Default)]
    public IActionResult GetCsrfToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append(
            _antiforgeryOptions.Value.RequestTokenCookieName,
            tokens.RequestToken!,
            new CookieOptions
            {
                HttpOnly = false,
                Path = _antiforgeryOptions.Value.Path,
                SameSite = Enum.Parse<SameSiteMode>(_antiforgeryOptions.Value.SameSite, true),
                Secure = Enum.Parse<CookieSecurePolicy>(_antiforgeryOptions.Value.SecurePolicy, true)
                    == CookieSecurePolicy.Always
            });

        return NoContent();
    }

    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LogoutResponse))]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken)
    {
        var postLogoutRedirectUri = GetSafeReturnUrl(request.ReturnUrl);
        var authentication = await HttpContext.AuthenticateAsync(AuthenticationSchemes.ApplicationCookie);
        var isLocalBypass = _environment.IsDevelopment()
            && authentication.Properties?.Items.ContainsKey(AuthenticationSchemes.LocalBypassProperty) == true;

        if (isLocalBypass)
        {
            await HttpContext.SignOutAsync(AuthenticationSchemes.ApplicationCookie);
            return Ok(new LogoutResponse { LogoutUrl = postLogoutRedirectUri });
        }

        var idToken = await HttpContext.GetTokenAsync(AuthenticationSchemes.ApplicationCookie, "id_token");
        var oidcOptions = _openIdConnectOptions.Get(AuthenticationSchemes.Keycloak);
        var oidcConfiguration = await oidcOptions.ConfigurationManager!.GetConfigurationAsync(cancellationToken);
        var endSessionEndpoint = oidcConfiguration.EndSessionEndpoint;

        if (string.IsNullOrWhiteSpace(endSessionEndpoint))
        {
            throw new InvalidOperationException("Keycloak end_session_endpoint is not configured.");
        }

        var parameters = new Dictionary<string, string?>
        {
            ["post_logout_redirect_uri"] = postLogoutRedirectUri,
            ["client_id"] = oidcOptions.ClientId
        };

        if (!string.IsNullOrWhiteSpace(idToken))
        {
            parameters["id_token_hint"] = idToken;
        }

        var logoutUrl = QueryHelpers.AddQueryString(endSessionEndpoint, parameters);

        await HttpContext.SignOutAsync(AuthenticationSchemes.ApplicationCookie);

        return Ok(new LogoutResponse { LogoutUrl = logoutUrl });
    }
}
