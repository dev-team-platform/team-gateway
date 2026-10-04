using System.Threading.Channels;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TeamGateway.Api.Options;

namespace TeamGateway.Api.Services.RabbitMq;

public sealed class RabbitMqPublisherHostedService : BackgroundService
{
    private readonly RabbitMqConnection _connection;
    private readonly RabbitMqTopologyInitializer _topology;
    private readonly IOptions<RabbitMqOptions> _options;
    private readonly Serilog.ILogger _logger;
    private readonly Channel<RabbitMqPublishRequest> _requests =
        Channel.CreateBounded<RabbitMqPublishRequest>(new BoundedChannelOptions(5000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public RabbitMqPublisherHostedService(
        RabbitMqConnection connection,
        RabbitMqTopologyInitializer topology,
        IOptions<RabbitMqOptions> options,
        Serilog.ILogger logger)
    {
        _connection = connection;
        _topology = topology;
        _options = options;
        _logger = logger;
    }

    public async Task PublishAsync(
        string publisherName,
        string routingKey,
        ReadOnlyMemory<byte> body,
        BasicProperties properties,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Publishers.TryGetValue(publisherName, out var publisher))
        {
            throw new ArgumentException(
                $"RabbitMQ publisher '{publisherName}' is not configured.",
                nameof(publisherName));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _requests.Writer.WriteAsync(
            new RabbitMqPublishRequest(
                publisher.Exchange,
                routingKey,
                body,
                properties,
                completion),
            cancellationToken);

        await completion.Task.WaitAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _topology.InitializeAsync(stoppingToken);
                await RunPublisherAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.Warning(exception, "RabbitMQ publisher disconnected; reconnecting");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _requests.Writer.TryComplete();
    }

    private async Task RunPublisherAsync(CancellationToken stoppingToken)
    {
        var connection = await _connection.GetConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            stoppingToken);

        await foreach (var request in _requests.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await channel.BasicPublishAsync(
                    exchange: request.Exchange,
                    routingKey: request.RoutingKey,
                    mandatory: true,
                    basicProperties: request.Properties,
                    body: request.Body,
                    cancellationToken: stoppingToken);

                request.Completion.TrySetResult();
            }
            catch (Exception exception)
            {
                request.Completion.TrySetException(exception);
                throw;
            }
        }
    }

    private sealed record RabbitMqPublishRequest(
        string Exchange,
        string RoutingKey,
        ReadOnlyMemory<byte> Body,
        BasicProperties Properties,
        TaskCompletionSource Completion);
}
