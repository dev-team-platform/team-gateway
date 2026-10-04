using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TeamGateway.Api.Models.Messaging.Publishing;
using TeamGateway.Api.Options;
using TeamGateway.Api.Services.RabbitMq;
using TeamGateway.Api.Services.RabbitMq.Publishers;

namespace TeamGateway.Api.Extensions;

public static class RabbitMqEventPublisherExtensions
{
    public static IServiceCollection AddRabbitMqEventPublisher(
        this IServiceCollection services)
    {
        services
            .AddOptions<RabbitMqOptions>()
            .BindConfiguration(RabbitMqOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.Host), "RabbitMq:Host is required.")
            .Validate(options => options.Port is > 0 and <= 65535, "RabbitMq:Port must be between 1 and 65535.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Username), "RabbitMq:Username is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Password), "RabbitMq:Password is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.VirtualHost), "RabbitMq:VirtualHost is required.")
            .ValidateOnStart();

        services.AddSingleton<IConnectionFactory>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
            return new ConnectionFactory
            {
                HostName = options.Host,
                Port = options.Port,
                UserName = options.Username,
                Password = options.Password,
                VirtualHost = options.VirtualHost,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true
            };
        });

        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<RabbitMqTopologyInitializer>();
        services.AddSingleton<RabbitMqPublisherHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<RabbitMqPublisherHostedService>());
        services.AddSingleton<
            IMessagingPublisherHandler<OutboxEvent<UserLoggedInEventData>>,
            OutboxEventPublisherHandler<UserLoggedInEventData>>();
        return services;
    }
}
