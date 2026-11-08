using System.Security.Claims;
using System.Net;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using TeamGateway.Api.Constants;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using TeamGateway.Api.Options;
using TeamGateway.Api.Services.Auth;
using TeamGateway.Api.Stores;
using Xunit;

namespace TeamGateway.Api.Tests.Services.Auth;

public class OidcTokenRefreshServiceTests
{
    private readonly Mock<IDistributedRefreshLock> _refreshLock = new();
    private readonly Mock<IDatabase> _database = new();
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IHttpClientFactory> _http = new();
    private readonly Mock<IOptionsMonitor<OpenIdConnectOptions>> _oidc = new();

    public OidcTokenRefreshServiceTests() => _redis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_database.Object);

    [Fact]
    public async Task RefreshIfNeededAsync_ShouldSkipRefresh_WhenAccessTokenIsNotNearExpiry()
    {
        var properties = Properties(DateTimeOffset.UtcNow.AddMinutes(20));

        var result = await CreateService().RefreshIfNeededAsync(properties, CancellationToken.None);

        result.Should().Be(TokenRefreshStatus.NotNeeded);
        _refreshLock.Verify(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshIfNeededAsync_ShouldFail_WhenSessionIdentifierIsMissing()
    {
        var result = await CreateService().RefreshIfNeededAsync(Properties(DateTimeOffset.UtcNow.AddSeconds(-1)), CancellationToken.None);

        result.Should().Be(TokenRefreshStatus.Failed);
        _refreshLock.Verify(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshIfNeededAsync_ShouldReportAlreadyRefreshing_WhenLockIsUnavailable()
    {
        var properties = Properties(DateTimeOffset.UtcNow.AddSeconds(-1));
        properties.SetString(".session", "session");
        _refreshLock.Setup(x => x.TryAcquireAsync("session", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAsyncDisposable?)null);

        var result = await CreateService().RefreshIfNeededAsync(properties, CancellationToken.None);

        result.Should().Be(TokenRefreshStatus.AlreadyRefreshing);
    }

    [Fact]
    public async Task RefreshIfNeededAsync_ShouldRemoveSession_WhenRefreshTokenIsMissing()
    {
        var properties = Properties(DateTimeOffset.UtcNow.AddSeconds(-1));
        properties.SetString(".session", "session");
        var lease = new Mock<IAsyncDisposable>();
        _refreshLock.Setup(x => x.TryAcquireAsync("session", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(lease.Object);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([], "test")), Properties(DateTimeOffset.UtcNow.AddSeconds(-1)), "test");
        _database.Setup(x => x.StringGetAsync("gateway:auth:session:session", It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)TicketSerializer.Default.Serialize(ticket));
        _database.Setup(x => x.KeyDeleteAsync("gateway:auth:session:session", It.IsAny<CommandFlags>())).ReturnsAsync(true);

        var result = await CreateService().RefreshIfNeededAsync(properties, CancellationToken.None);

        result.Should().Be(TokenRefreshStatus.Failed);
        _database.Verify(x => x.KeyDeleteAsync("gateway:auth:session:session", It.IsAny<CommandFlags>()), Times.Once);
        lease.Verify(x => x.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task RefreshIfNeededAsync_ShouldStoreRotatedTokens_WhenProviderReturnsStandardOAuthResponse()
    {
        var properties = Properties(DateTimeOffset.UtcNow.AddSeconds(-1));
        properties.SetString(".session", "session");
        var storedProperties = Properties(DateTimeOffset.UtcNow.AddSeconds(-1));
        storedProperties.StoreTokens([
            new AuthenticationToken { Name = "expires_at", Value = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O") },
            new AuthenticationToken { Name = "refresh_token", Value = "old-refresh" }
        ]);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([], "test")), storedProperties, "test");
        _database.Setup(x => x.StringGetAsync("gateway:auth:session:session", It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)TicketSerializer.Default.Serialize(ticket));
        _database.Setup(x => x.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(), It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        var lease = new Mock<IAsyncDisposable>();
        _refreshLock.Setup(x => x.TryAcquireAsync("session", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(lease.Object);
        var configuration = new Mock<IConfigurationManager<OpenIdConnectConfiguration>>();
        configuration.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenIdConnectConfiguration { TokenEndpoint = "https://identity.test/token" });
        var oidc = new OpenIdConnectOptions { ConfigurationManager = configuration.Object };
        _oidc.Setup(x => x.Get(TeamGateway.Api.Constants.AuthenticationSchemes.Keycloak)).Returns(oidc);
        var handler = new RefreshHandler();
        _http.Setup(x => x.CreateClient(nameof(OidcTokenRefreshService))).Returns(new HttpClient(handler));

        var result = await CreateService().RefreshIfNeededAsync(properties, CancellationToken.None);

        result.Should().Be(TokenRefreshStatus.Refreshed);
        properties.GetTokenValue("access_token").Should().Be("new-access");
        properties.GetTokenValue("refresh_token").Should().Be("new-refresh");
        properties.GetTokenValue("id_token").Should().Be("new-id");
        DateTimeOffset.Parse(properties.GetTokenValue("expires_at")!).Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(4));
        handler.Form.Should().Contain("grant_type=refresh_token").And.Contain("refresh_token=old-refresh");
        _database.Verify(x => x.StringSetAsync("gateway:auth:session:session", It.IsAny<RedisValue>(), It.IsAny<Expiration>(), It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()), Times.Once);
        lease.Verify(x => x.DisposeAsync(), Times.Once);
    }

    private sealed class RefreshHandler : HttpMessageHandler
    {
        public string Form { get; private set; } = string.Empty;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Form = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"id_token\":\"new-id\",\"expires_in\":300}", System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    private OidcTokenRefreshService CreateService() => new(
        _refreshLock.Object, new RedisTicketStore(_redis.Object), _http.Object, _oidc.Object,
        Microsoft.Extensions.Options.Options.Create(new AuthOptions { Keycloak = new KeycloakOptions { RefreshBeforeExpiry = TimeSpan.FromMinutes(2), RefreshLockTimeout = TimeSpan.FromSeconds(30) } }),
        NullLogger<OidcTokenRefreshService>.Instance);

    private static AuthenticationProperties Properties(DateTimeOffset expires)
    {
        var properties = new AuthenticationProperties();
        properties.StoreTokens([new AuthenticationToken { Name = "expires_at", Value = expires.ToString("O") }]);
        return properties;
    }
}
