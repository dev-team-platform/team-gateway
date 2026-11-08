namespace TeamGateway.Api.Models.Messaging.Publishing;

public class OutboxEvent<TModel>
{
    public required string RoutingKey { get; init; }
    public required string Type { get; init; }
    public required TModel Data { get; init; }
}
