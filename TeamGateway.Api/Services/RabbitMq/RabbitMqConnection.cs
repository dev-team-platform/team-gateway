using RabbitMQ.Client;

namespace TeamGateway.Api.Services.RabbitMq;

public sealed class RabbitMqConnection : IAsyncDisposable
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;
    private bool _disposed;

    public RabbitMqConnection(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lock.WaitAsync();

        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
        finally
        {
            _lock.Release();
        }

        _lock.Dispose();
    }
}
