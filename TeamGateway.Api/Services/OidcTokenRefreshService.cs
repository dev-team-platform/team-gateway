using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using TeamGateway.Api.Constants;
using TeamGateway.Api.Options;
using TeamGateway.Api.Stores;

namespace TeamGateway.Api.Services;

public enum TokenRefreshStatus
{
    NotNeeded,
    Refreshed,
    AlreadyRefreshing,
    Failed
}

public interface IOidcTokenRefreshService
{
    Task<TokenRefreshStatus> RefreshIfNeededAsync(
        AuthenticationProperties properties,
        CancellationToken cancellationToken);
}

public sealed class OidcTokenRefreshService : IOidcTokenRefreshService
{
    private const string SessionKeyProperty = ".session";
    private readonly IDistributedRefreshLock _refreshLock;
    private readonly RedisTicketStore _ticketStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OpenIdConnectOptions> _oidcOptions;
    private readonly IOptions<AuthOptions> _authOptions;
    private readonly ILogger<OidcTokenRefreshService> _logger;

    public OidcTokenRefreshService(
        IDistributedRefreshLock refreshLock,
        RedisTicketStore ticketStore,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
        IOptions<AuthOptions> authOptions,
        ILogger<OidcTokenRefreshService> logger)
    {
        _refreshLock = refreshLock;
        _ticketStore = ticketStore;
        _httpClientFactory = httpClientFactory;
        _oidcOptions = oidcOptions;
        _authOptions = authOptions;
        _logger = logger;
    }

    public async Task<TokenRefreshStatus> RefreshIfNeededAsync(
        AuthenticationProperties properties,
        CancellationToken cancellationToken)
    {
        if (!NeedsRefresh(properties))
        {
            return TokenRefreshStatus.NotNeeded;
        }

        var sessionId = properties.GetString(SessionKeyProperty);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return TokenRefreshStatus.Failed;
        }

        await using var lease = await _refreshLock.TryAcquireAsync(
            sessionId,
            _authOptions.Value.Keycloak.RefreshLockTimeout,
            cancellationToken);

        if (lease is null)
        {
            return TokenRefreshStatus.AlreadyRefreshing;
        }

        var ticket = await _ticketStore.RetrieveAsync(sessionId);
        if (ticket is null)
        {
            return TokenRefreshStatus.Failed;
        }

        if (!NeedsRefresh(ticket.Properties))
        {
            CopyTokenProperties(ticket.Properties, properties);
            return TokenRefreshStatus.NotNeeded;
        }

        var refreshToken = ticket.Properties.GetTokenValue("refresh_token");
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            await _ticketStore.RemoveAsync(sessionId);
            return TokenRefreshStatus.Failed;
        }

        try
        {
            var response = await RequestRefreshAsync(refreshToken, cancellationToken);
            if (response is null || string.IsNullOrWhiteSpace(response.AccessToken))
            {
                await _ticketStore.RemoveAsync(sessionId);
                return TokenRefreshStatus.Failed;
            }

            StoreRefreshedTokens(ticket.Properties, response, refreshToken);
            CopyTokenProperties(ticket.Properties, properties);
            await _ticketStore.RenewAsync(sessionId, ticket);
            return TokenRefreshStatus.Refreshed;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "OIDC token refresh failed for gateway session {SessionId}", sessionId);
            await _ticketStore.RemoveAsync(sessionId);
            return TokenRefreshStatus.Failed;
        }
    }

    private bool NeedsRefresh(AuthenticationProperties properties)
    {
        var expiresAt = properties.GetTokenValue("expires_at");
        return !DateTimeOffset.TryParse(expiresAt, out var expiresUtc)
            || expiresUtc <= DateTimeOffset.UtcNow.Add(_authOptions.Value.Keycloak.RefreshBeforeExpiry);
    }

    private async Task<TokenRefreshResponse?> RequestRefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var oidcOptions = _oidcOptions.Get(AuthenticationSchemes.Keycloak);
        var configuration = await oidcOptions.ConfigurationManager!
            .GetConfigurationAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _authOptions.Value.Keycloak.ClientId,
                ["client_secret"] = _authOptions.Value.Keycloak.ClientSecret,
                ["refresh_token"] = refreshToken
            })
        };

        var client = _httpClientFactory.CreateClient(nameof(OidcTokenRefreshService));
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogInformation("OIDC token refresh endpoint returned {StatusCode}", response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TokenRefreshResponse>(cancellationToken);
    }

    private static void StoreRefreshedTokens(
        AuthenticationProperties properties,
        TokenRefreshResponse response,
        string previousRefreshToken)
    {
        var tokens = properties.GetTokens()
            .ToDictionary(token => token.Name, token => token.Value, StringComparer.Ordinal);

        tokens["access_token"] = response.AccessToken;
        tokens["refresh_token"] = response.RefreshToken ?? previousRefreshToken;
        tokens["expires_at"] = DateTimeOffset.UtcNow
            .AddSeconds(response.ExpiresIn)
            .ToString("O");

        if (!string.IsNullOrWhiteSpace(response.IdToken))
        {
            tokens["id_token"] = response.IdToken;
        }

        properties.StoreTokens(tokens.Select(pair => new AuthenticationToken
        {
            Name = pair.Key,
            Value = pair.Value
        }));
    }

    private static void CopyTokenProperties(
        AuthenticationProperties source,
        AuthenticationProperties destination)
    {
        destination.StoreTokens(source.GetTokens());
    }

    private sealed class TokenRefreshResponse
    {
        public string AccessToken { get; init; } = null!;
        public string? RefreshToken { get; init; }
        public string? IdToken { get; init; }
        public int ExpiresIn { get; init; }
    }
}
