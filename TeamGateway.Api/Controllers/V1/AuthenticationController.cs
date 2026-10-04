using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TeamGateway.Api.Attributes;
using TeamGateway.Api.Constants;
using TeamGateway.Api.Options;

namespace TeamGateway.Api.Controllers.V1;

[Route("api/v{version:apiVersion}/auth")]
[ApiVersion("1.0")]
[ApiController]
[EnableRateLimiting(RateLimiterPolicies.Auth)]
public class AuthenticationController : ControllerBase
{
    private readonly IAntiforgery _antiforgery;
    private readonly IOptions<Options.AntiforgeryOptions> _antiforgeryOptions;
    private readonly IConfiguration _configuration;

    public AuthenticationController(
        IAntiforgery antiforgery,
        IOptions<Options.AntiforgeryOptions> antiforgeryOptions,
        IConfiguration configuration)
    {
        _antiforgery = antiforgery;
        _antiforgeryOptions = antiforgeryOptions;
        _configuration = configuration;
    }

    [HttpGet("login")]
    [SkipClientCorrelation]
    public IActionResult Login([FromQuery] string returnUrl)
    {
        var redirectUri = GetSafeReturnUrl(returnUrl);

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
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthenticationSchemes.ApplicationCookie);
        return NoContent();
    }
}
