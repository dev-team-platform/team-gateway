namespace TeamGateway.Api.Services.RabbitMq.Publishers;

public interface IMessagingPublisherHandler<in TMessage>
{
    Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}
