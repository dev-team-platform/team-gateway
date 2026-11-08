using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TeamGateway.Api.Options;

namespace TeamGateway.Api.Services.RabbitMq;

public sealed class RabbitMqTopologyInitializer
{
    private readonly RabbitMqConnection _connection;
    private readonly IOptions<RabbitMqOptions> _options;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public RabbitMqTopologyInitializer(
        RabbitMqConnection connection,
        IOptions<RabbitMqOptions> options)
    {
        _connection = connection;
        _options = options;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            var connection = await _connection.GetConnectionAsync(cancellationToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            var declaredExchanges = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var publisher in _options.Value.Publishers.Values)
            {
                if (declaredExchanges.TryGetValue(publisher.Exchange, out var exchangeType))
                {
                    if (!string.Equals(exchangeType, publisher.ExchangeType, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"RabbitMQ exchange '{publisher.Exchange}' has conflicting configured types.");
                    }

                    continue;
                }

                await channel.ExchangeDeclareAsync(
                    exchange: publisher.Exchange,
                    type: publisher.ExchangeType,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: cancellationToken);

                declaredExchanges.Add(publisher.Exchange, publisher.ExchangeType);
            }

            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }
}
