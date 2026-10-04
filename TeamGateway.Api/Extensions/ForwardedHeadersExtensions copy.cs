using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using StackExchange.Redis;

namespace TeamGateway.Api.Extensions;

public static class ForwardedHeadersExtensions
{
    public static IServiceCollection AddForwardedHeadersConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddForwardedHeadersConfigurationOptions(configuration);

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedProto;

            var config = configuration
                .GetRequiredSection(Options.ForwardedHeadersOptions.SectionName)
                .Get<Options.ForwardedHeadersOptions>()!;

            foreach (var proxy in config.KnownProxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }

            foreach (var network in config.KnownNetworks)
            {
                var parts = network.Split('/');

                var prefix = IPAddress.Parse(parts[0]);
                var prefixLength = int.Parse(parts[1]);

                options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, prefixLength));
            }
        });

        return services;
    }

    private static void AddForwardedHeadersConfigurationOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<Options.ForwardedHeadersOptions>()
            .Bind(configuration.GetRequiredSection(Options.ForwardedHeadersOptions.SectionName))
            .ValidateOnStart();
    }
}
