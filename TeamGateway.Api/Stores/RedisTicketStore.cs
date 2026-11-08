using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using StackExchange.Redis;

namespace TeamGateway.Api.Stores;

public sealed class RedisTicketStore : ITicketStore
{
    private const string SessionPrefix = "gateway:auth:session:";
    private readonly IDatabase _database;

    public RedisTicketStore(IConnectionMultiplexer multiplexer)
    {
        _database = multiplexer.GetDatabase();
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Guid.NewGuid().ToString("N");
        await RenewAsync(key, ticket);
        return key;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var lifetime = ticket.Properties.ExpiresUtc.HasValue
            ? ticket.Properties.ExpiresUtc.Value - DateTimeOffset.UtcNow
            : TimeSpan.FromHours(8);

        if (lifetime <= TimeSpan.Zero)
        {
            lifetime = TimeSpan.FromMinutes(1);
        }

        var bytes = TicketSerializer.Default.Serialize(ticket);
        await _database.StringSetAsync(SessionKey(key), bytes, lifetime);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var bytes = await _database.StringGetAsync(SessionKey(key));
        return bytes.IsNullOrEmpty ? null : TicketSerializer.Default.Deserialize(bytes!);
    }

    public Task RemoveAsync(string key) => _database.KeyDeleteAsync(SessionKey(key));

    private static RedisKey SessionKey(string key) => $"{SessionPrefix}{key}";
}
