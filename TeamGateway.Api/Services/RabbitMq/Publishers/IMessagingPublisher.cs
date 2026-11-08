namespace TeamGateway.Api.Services.RabbitMq.Publishers;

public interface IMessagingPublisher<in TMessage>
{
    Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}
