using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Moq;
using StackExchange.Redis;
using TeamGateway.Api.Stores;
using Xunit;

namespace TeamGateway.Api.Tests.Services.Auth;

public class RedisTicketStoreTests
{
    private readonly Mock<IDatabase> _database = new();
    private readonly Mock<IConnectionMultiplexer> _connection = new();

    public RedisTicketStoreTests() => _connection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_database.Object);

    [Fact]
    public async Task RenewAsync_ShouldStoreSerializedTicketWithRemainingLifetime_WhenTicketHasExpiry()
    {
        var ticket = CreateTicket(DateTimeOffset.UtcNow.AddMinutes(30));
        RedisValue stored = default;
        var lifetime = Expiration.Default;
        _database.Setup(x => x.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(), It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, Expiration, ValueCondition, CommandFlags>((_, value, expiry, _, _) => { stored = value; lifetime = expiry; })
            .ReturnsAsync(true);

        await new RedisTicketStore(_connection.Object).RenewAsync("session", ticket);

        lifetime.Should().NotBe(Expiration.Default);
        TicketSerializer.Default.Deserialize((byte[])stored!)!.Principal.FindFirst("sub")!.Value.Should().Be("user");
        _database.Verify(x => x.StringSetAsync("gateway:auth:session:session", It.IsAny<RedisValue>(), It.IsAny<Expiration>(), It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RetrieveAsync_ShouldReturnTicket_WhenSessionExists()
    {
        var bytes = TicketSerializer.Default.Serialize(CreateTicket(null));
        _database.Setup(x => x.StringGetAsync("gateway:auth:session:session", It.IsAny<CommandFlags>())).ReturnsAsync((RedisValue)bytes);

        var result = await new RedisTicketStore(_connection.Object).RetrieveAsync("session");

        result!.Principal.FindFirst("sub")!.Value.Should().Be("user");
    }

    [Fact]
    public async Task RetrieveAsync_ShouldReturnNull_WhenSessionDoesNotExist()
    {
        _database.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(RedisValue.Null);
        (await new RedisTicketStore(_connection.Object).RetrieveAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_ShouldDeleteSession_WhenCalled()
    {
        _database.Setup(x => x.KeyDeleteAsync("gateway:auth:session:session", It.IsAny<CommandFlags>())).ReturnsAsync(true);
        await new RedisTicketStore(_connection.Object).RemoveAsync("session");
        _database.Verify(x => x.KeyDeleteAsync("gateway:auth:session:session", It.IsAny<CommandFlags>()), Times.Once);
    }

    private static AuthenticationTicket CreateTicket(DateTimeOffset? expires) => new(
        new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user")], "test")),
        new AuthenticationProperties { ExpiresUtc = expires }, "test");
}
