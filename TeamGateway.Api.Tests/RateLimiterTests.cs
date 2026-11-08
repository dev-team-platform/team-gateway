using System.Net;
using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using GatewayRateLimiterOptions = TeamGateway.Api.Options.RateLimiterOptions;
using TeamGateway.Api.Constants;
using TeamGateway.Api.Extensions;
using Xunit;

namespace TeamGateway.Api.Tests;

public class RateLimiterTests
{
    [Fact]
    public async Task MapReverseProxy_ShouldStart_WhenDevelopmentRoutesUseRegisteredRateLimiterPolicies()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json")
            .Build();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("gateway-session", policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddGatewayRateLimiter(configuration);
        builder.Services.AddGatewayReverseProxy(configuration);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapReverseProxy();
        await app.StartAsync();

        configuration["ReverseProxy:Routes:notification-api:RateLimiterPolicy"]
            .Should().Be(RateLimiterPolicies.Standard);
        configuration["ReverseProxy:Routes:organization-api:RateLimiterPolicy"]
            .Should().Be(RateLimiterPolicies.Standard);
    }

    [Fact]
    public async Task StandardPolicy_ShouldShareQuotaAcrossIpAddresses_WhenSubjectIsTheSame()
    {
        await using var app = await CreateServerAsync();
        using var client = app.GetTestClient();

        (await SendAsync(client, "subject-a", "192.0.2.1")).Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, "subject-a", "192.0.2.2")).Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task StandardPolicy_ShouldUseSeparateQuotas_WhenSubjectsShareAnIpAddress()
    {
        await using var app = await CreateServerAsync();
        using var client = app.GetTestClient();

        (await SendAsync(client, "subject-a", "192.0.2.1")).Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, "subject-b", "192.0.2.1")).Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, "subject-a", "192.0.2.1")).Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task StandardPolicy_ShouldShareAnonymousQuota_WhenNoAuthenticatedSubjectExists()
    {
        await using var app = await CreateServerAsync();
        using var client = app.GetTestClient();

        (await SendAsync(client, null, "192.0.2.1")).Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, null, "192.0.2.2")).Should().Be(HttpStatusCode.TooManyRequests);
        (await SendAsync(client, "subject-a", "192.0.2.1")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StandardPolicy_ShouldReplenishQuota_WhenWindowElapses()
    {
        await using var app = await CreateServerAsync(window: "00:00:01");
        using var client = app.GetTestClient();

        (await SendAsync(client, "subject-a", "192.0.2.1")).Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, "subject-a", "192.0.2.1")).Should().Be(HttpStatusCode.TooManyRequests);
        await Task.Delay(TimeSpan.FromSeconds(2));
        (await SendAsync(client, "subject-a", "192.0.2.1")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuthPolicy_ShouldUseSeparateQuotas_WhenIpAddressesDiffer()
    {
        await using var app = await CreateServerAsync();
        using var client = app.GetTestClient();

        (await SendAsync(client, null, "192.0.2.1", "/auth")).Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, null, "192.0.2.1", "/auth")).Should().Be(HttpStatusCode.TooManyRequests);
        (await SendAsync(client, null, "192.0.2.2", "/auth")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExportPolicy_ShouldUseSeparateSubjectQuotas_WhenUsersShareAnIpAddress()
    {
        await using var app = await CreateServerAsync();
        using var client = app.GetTestClient();

        (await SendAsync(client, "subject-a", "192.0.2.1", "/export")).Should().Be(HttpStatusCode.OK);
        (await SendAsync(client, "subject-a", "192.0.2.2", "/export")).Should().Be(HttpStatusCode.TooManyRequests);
        (await SendAsync(client, "subject-b", "192.0.2.1", "/export")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void AddGatewayRateLimiter_ShouldRejectInvalidConfiguration_WhenSlidingWindowHasNoSegments()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiter:Standard:PermitLimit"] = "1",
            ["RateLimiter:Standard:Window"] = "00:01:00",
            ["RateLimiter:Standard:SegmentsPerWindow"] = "0",
            ["RateLimiter:Auth:PermitLimit"] = "1",
            ["RateLimiter:Auth:Window"] = "00:01:00",
            ["RateLimiter:Auth:SegmentsPerWindow"] = "2"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddGatewayRateLimiter(configuration);
        using var provider = services.BuildServiceProvider();

        var action = () => provider.GetRequiredService<IOptions<GatewayRateLimiterOptions>>().Value;

        action.Should().Throw<OptionsValidationException>()
            .WithMessage("*RateLimiter:Standard:SegmentsPerWindow must be positive*");
    }

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, string? subject, string ip, string path = "/standard")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("test-ip", ip);
        if (subject is not null)
            request.Headers.Add("test-subject", subject);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static async Task<WebApplication> CreateServerAsync(string window = "00:01:00")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiter:Standard:PermitLimit"] = "1",
            ["RateLimiter:Standard:Window"] = window,
            ["RateLimiter:Standard:SegmentsPerWindow"] = "2",
            ["RateLimiter:Standard:QueueLimit"] = "0",
            ["RateLimiter:Auth:PermitLimit"] = "1",
            ["RateLimiter:Auth:Window"] = "00:01:00",
            ["RateLimiter:Auth:SegmentsPerWindow"] = "2",
            ["RateLimiter:Auth:QueueLimit"] = "0",
            ["RateLimiter:Export:PermitLimit"] = "1",
            ["RateLimiter:Export:Window"] = "00:01:00",
            ["RateLimiter:Export:SegmentsPerWindow"] = "2",
            ["RateLimiter:Export:QueueLimit"] = "0"
        }).Build();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddGatewayRateLimiter(configuration);
        var app = builder.Build();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers["test-ip"].ToString());
            var subject = context.Request.Headers["test-subject"].ToString();
            if (!string.IsNullOrEmpty(subject))
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], "test"));
            await next();
        });
        app.UseRateLimiter();
        app.MapGet("/standard", () => Results.Ok()).RequireRateLimiting(RateLimiterPolicies.Standard);
        app.MapGet("/auth", () => Results.Ok()).RequireRateLimiting(RateLimiterPolicies.Auth);
        app.MapGet("/export", () => Results.Ok()).RequireRateLimiting(RateLimiterPolicies.Export);
        await app.StartAsync();
        return app;
    }
}
