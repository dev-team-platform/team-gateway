using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using TeamGateway.Api.Constants;
using TeamGateway.Api.Options;
using TeamGateway.Api.Services;
using TeamGateway.Api.Stores;

namespace TeamGateway.Api.Extensions;

public static class GatewaySecurityExtensions
{
    public static IServiceCollection AddGatewaySecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddGatewaySecurityOptions(configuration);

        var auth = configuration.GetRequiredSection(AuthOptions.SectionName).Get<AuthOptions>()!;
        var antiforgery = configuration.GetRequiredSection(AntiforgeryOptions.SectionName).Get<AntiforgeryOptions>()!;

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(configuration["Redis:ConnectionString"]!);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<RedisTicketStore>();
        services.AddSingleton<IDistributedRefreshLock, DistributedRefreshLock>();
        services.AddSingleton<IInternalJwtIssuer, InternalJwtIssuer>();
        services.AddScoped<IOidcTokenRefreshService, OidcTokenRefreshService>();
        services.AddHttpClient(nameof(OidcTokenRefreshService));

        services.AddAntiforgery(options =>
        {
            options.HeaderName = antiforgery.HeaderName;
            options.Cookie.Name = antiforgery.CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.Path = antiforgery.Path;
            options.Cookie.SameSite = Enum.Parse<SameSiteMode>(antiforgery.SameSite, true);
            options.Cookie.SecurePolicy = Enum.Parse<CookieSecurePolicy>(antiforgery.SecurePolicy, true);
        });

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AuthenticationSchemes.ApplicationCookie;
                options.DefaultSignInScheme = AuthenticationSchemes.ApplicationCookie;
                options.DefaultChallengeScheme = AuthenticationSchemes.Keycloak;
            })
            .AddCookie(AuthenticationSchemes.ApplicationCookie, options =>
            {
                options.Cookie.Name = auth.Cookie.CookieName;
                options.Cookie.HttpOnly = auth.Cookie.HttpOnly;
                options.Cookie.Path = auth.Cookie.Path;
                options.Cookie.SameSite = Enum.Parse<SameSiteMode>(auth.Cookie.SameSite, true);
                options.Cookie.SecurePolicy = Enum.Parse<CookieSecurePolicy>(auth.Cookie.SecurePolicy, true);
                options.ExpireTimeSpan = auth.Cookie.ExpireTimeSpan;
                options.SlidingExpiration = auth.Cookie.SlidingExpiration;
                options.LoginPath = "/api/v1/auth/login";
                options.AccessDeniedPath = "/api/v1/auth/access-denied";
                options.Events.OnValidatePrincipal = async context =>
                {
                    var refresher = context.HttpContext.RequestServices
                        .GetRequiredService<IOidcTokenRefreshService>();
                    var status = await refresher.RefreshIfNeededAsync(
                        context.Properties,
                        context.HttpContext.RequestAborted);

                    if (status == TokenRefreshStatus.Failed)
                    {
                        context.RejectPrincipal();
                    }
                    else if (status == TokenRefreshStatus.Refreshed)
                    {
                        context.ShouldRenew = true;
                    }
                };
            })
            .AddOpenIdConnect(AuthenticationSchemes.Keycloak, options =>
            {
                options.Authority = auth.Keycloak.Authority;
                options.ClientId = auth.Keycloak.ClientId;
                options.ClientSecret = auth.Keycloak.ClientSecret;
                options.CallbackPath = auth.Keycloak.CallbackPath;
                options.SignedOutCallbackPath = auth.Keycloak.SignedOutCallbackPath;
                options.SignInScheme = AuthenticationSchemes.ApplicationCookie;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                options.MapInboundClaims = false;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "preferred_username",
                    RoleClaimType = "roles"
                };
            });

        services
            .AddOptions<CookieAuthenticationOptions>(AuthenticationSchemes.ApplicationCookie)
            .Configure<RedisTicketStore>((options, ticketStore) => options.SessionStore = ticketStore);

        return services;
    }

    private static void AddGatewaySecurityOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RedisOptions>().BindConfiguration(RedisOptions.SectionName)
            .Validate(x => !string.IsNullOrWhiteSpace(x.ConnectionString), "Redis:ConnectionString is required.")
            .ValidateOnStart();

        services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName)
            .Validate(x => x.AllowRedirectOrigins != null && x.AllowRedirectOrigins.Count > 0, "Auth:AllowRedirectOrigins must contain at least one origin.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.Cookie.CookieName), "Auth:Cookie:CookieName is required.")
            .Validate(x => x.Cookie.ExpireTimeSpan > TimeSpan.Zero, "Auth:Cookie:ExpireTimeSpan must be positive.")

            .Validate(x => !string.IsNullOrWhiteSpace(x.Keycloak.Authority), "Auth:Keycloak:Authority is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.Keycloak.ClientId), "Auth:Keycloak:ClientId is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.Keycloak.ClientSecret), "Auth:Keycloak:ClientSecret is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.Keycloak.CallbackPath), "Auth:Keycloak:CallbackPath is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.Keycloak.SignedOutCallbackPath), "Auth:Keycloak:SignedOutCallbackPath is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.Keycloak.ChangePasswordAction), "Auth:Keycloak:ChangePasswordAction is required.")
            .Validate(x => x.Keycloak.RefreshBeforeExpiry > TimeSpan.Zero, "Auth:Keycloak:RefreshBeforeExpiry must be positive.")
            .Validate(x => x.InternalJwt.AudienceMappings != null && x.InternalJwt.AudienceMappings.Count > 0, "Auth:InternalJwt:AudienceMappings is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.InternalJwt.PrivateKeyPemPath), "Auth:InternalJwt:PrivateKeyPem is required.")
            .Validate(x => x.InternalJwt.Lifetime > TimeSpan.Zero, "Auth:InternalJwt:Lifetime must be positive.")
            .ValidateOnStart();

        services.AddOptions<AntiforgeryOptions>().BindConfiguration(AntiforgeryOptions.SectionName)
            .Validate(x => !string.IsNullOrWhiteSpace(x.HeaderName), "Antiforgery:HeaderName is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.CookieName), "Antiforgery:CookieName is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.RequestTokenCookieName), "Antiforgery:RequestTokenCookieName is required.")
            .ValidateOnStart();
    }
}
