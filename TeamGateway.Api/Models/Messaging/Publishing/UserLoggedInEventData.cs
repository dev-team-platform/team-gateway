namespace TeamGateway.Api.Models.Messaging.Publishing;

public sealed class UserLoggedInEventData
{
    public const string RoutingKey = "user.logged-in.v1";

    public required string IdentitySubject { get; init; }
    public required DateTimeOffset LastLoginAt { get; init; }
}
