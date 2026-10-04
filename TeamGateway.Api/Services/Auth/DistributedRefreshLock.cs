using StackExchange.Redis;

namespace TeamGateway.Api.Services.Auth;

public interface IDistributedRefreshLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(string sessionId, TimeSpan expiry, CancellationToken cancellationToken);
}

public sealed class DistributedRefreshLock : IDistributedRefreshLock
{
    private const string Prefix = "gateway:auth:refresh-lock:";
    private readonly IDatabase _database;

    public DistributedRefreshLock(IConnectionMultiplexer multiplexer)
    {
        _database = multiplexer.GetDatabase();
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string sessionId,
        TimeSpan expiry,
        CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");
        var key = (RedisKey)$"{Prefix}{sessionId}";

        if (!await _database.StringSetAsync(key, token, expiry, When.NotExists))
        {
            return null;
        }

        return new Lease(_database, key, token);
    }

    private sealed class Lease : IAsyncDisposable
    {
        private const string CompareAndDelete = """
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('del', KEYS[1])
            end
            return 0
            """;

        private readonly IDatabase _database;
        private readonly RedisKey _key;
        private readonly RedisValue _token;

        public Lease(IDatabase database, RedisKey key, RedisValue token)
        {
            _database = database;
            _key = key;
            _token = token;
        }

        public ValueTask DisposeAsync() => new(_database.ScriptEvaluateAsync(
            CompareAndDelete,
            [_key],
            [_token]));
    }
}
