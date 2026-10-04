using System.Text.Json;
using RabbitMQ.Client;
using TeamGateway.Api.Models.Messaging.Publishing;


namespace TeamGateway.Api.Services.RabbitMq.Publishers;

public sealed class OutboxEventPublisherHandler<TModel> : IMessagingPublisherHandler<OutboxEvent<TModel>>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const string PublisherName = "GatewayEvents";

    private readonly RabbitMqPublisherHostedService _publisher;
    private readonly Serilog.ILogger _logger;

    public OutboxEventPublisherHandler(
        RabbitMqPublisherHostedService publisher,
        Serilog.ILogger logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    public async Task HandleAsync(
        OutboxEvent<TModel> outboxEvent,
        CancellationToken cancellationToken = default)
    {
        var messageId = Guid.CreateVersion7();
        var envelope = new OutboxEventEnvelope<TModel>
        {
            Id = messageId,
            Source = "team-gateway",
            Type = outboxEvent.Type,
            Data = outboxEvent.Data
        };

        try
        {
            await _publisher.PublishAsync(
                PublisherName,
                outboxEvent.RoutingKey,
                JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions),
                new BasicProperties
                {
                    ContentType = "application/cloudevents+json",
                    DeliveryMode = DeliveryModes.Persistent,
                    MessageId = messageId.ToString(),
                    Type = outboxEvent.Type
                },
                cancellationToken);

            _logger.Information(
                "Published Gateway integration event {MessageId} {MessageType} with routing key {RoutingKey}",
                messageId,
                outboxEvent.Type,
                outboxEvent.RoutingKey);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error(
                exception,
                "Unable to publish Gateway integration event {MessageType} with routing key {RoutingKey}",
                outboxEvent.Type,
                outboxEvent.RoutingKey);
        }
    }

    private sealed class OutboxEventEnvelope<TData>
    {
        public Guid Id { get; init; }
        public string Source { get; init; } = null!;
        public string Type { get; init; } = null!;
        public TData Data { get; init; } = default!;
    }
}
