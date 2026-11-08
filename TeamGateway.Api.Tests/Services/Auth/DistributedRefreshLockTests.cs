using AwesomeAssertions;
using Moq;
using StackExchange.Redis;
using TeamGateway.Api.Services.Auth;
using Xunit;

namespace TeamGateway.Api.Tests.Services.Auth;

public class DistributedRefreshLockTests
{
    [Fact]
    public async Task TryAcquireAsync_ShouldReturnNull_WhenLockAlreadyExists()
    {
        var database = new Mock<IDatabase>();
        var connection = new Mock<IConnectionMultiplexer>();
        connection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
        database.Setup(x => x.StringSetAsync("gateway:auth:refresh-lock:session", It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists))
            .ReturnsAsync(false);

        var result = await new DistributedRefreshLock(connection.Object).TryAcquireAsync("session", TimeSpan.FromSeconds(30), CancellationToken.None);

        result.Should().BeNull();
        database.Verify(x => x.StringSetAsync("gateway:auth:refresh-lock:session", It.IsAny<RedisValue>(), TimeSpan.FromSeconds(30), When.NotExists), Times.Once);
    }

    [Fact]
    public async Task TryAcquireAsync_ShouldReleaseOnlyOwnedLock_WhenLeaseIsDisposed()
    {
        var database = new Mock<IDatabase>();
        var connection = new Mock<IConnectionMultiplexer>();
        connection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
        RedisValue token = default;
        database.Setup(x => x.StringSetAsync("gateway:auth:refresh-lock:session", It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists))
            .Callback<RedisKey, RedisValue, TimeSpan?, When>((_, value, _, _) => token = value).ReturnsAsync(true);
        database.Setup(x => x.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)1L));

        var lease = await new DistributedRefreshLock(connection.Object).TryAcquireAsync("session", TimeSpan.FromSeconds(30), CancellationToken.None);
        await lease!.DisposeAsync();

        token.IsNullOrEmpty.Should().BeFalse();
        database.Verify(x => x.ScriptEvaluateAsync(It.Is<string>(script => script.Contains("redis.call('get'") && script.Contains("redis.call('del'")),
            It.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0] == (RedisKey)"gateway:auth:refresh-lock:session"),
            It.Is<RedisValue[]>(values => values.Length == 1 && values[0] == token), It.IsAny<CommandFlags>()), Times.Once);
    }
}
