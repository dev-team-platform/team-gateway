using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TeamGateway.Api.Attributes;
using TeamGateway.Api.Constants;
using TeamGateway.Api.Middlewares;
using Xunit;

namespace TeamGateway.Api.Tests.Middlewares;

public class ClientCorrelationMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldContinue_WhenBothHeadersContainUuids()
    {
        var context = CreateContext();
        context.Request.Headers[ClientCorrelationHeaders.ClientActionId] = Guid.NewGuid().ToString();
        context.Request.Headers[ClientCorrelationHeaders.ClientRequestId] = Guid.NewGuid().ToString();
        var called = false;

        await new ClientCorrelationMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, null, "X-Client-Action-Id")]
    [InlineData("a8d2c3c4-5b53-4db3-aaad-6ec07f49be03", null, "X-Client-Request-Id")]
    [InlineData("bad", "a8d2c3c4-5b53-4db3-aaad-6ec07f49be03", "X-Client-Action-Id")]
    [InlineData("a8d2c3c4-5b53-4db3-aaad-6ec07f49be03", "bad", "X-Client-Request-Id")]
    public async Task InvokeAsync_ShouldReturnBadRequest_WhenCorrelationHeaderIsMissingOrInvalid(string? actionId, string? requestId, string expectedHeader)
    {
        var context = CreateContext();
        if (actionId is not null) context.Request.Headers[ClientCorrelationHeaders.ClientActionId] = actionId;
        if (requestId is not null) context.Request.Headers[ClientCorrelationHeaders.ClientRequestId] = requestId;
        var called = false;

        await new ClientCorrelationMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        called.Should().BeFalse();
        context.Response.Body.Position = 0;
        (await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync()).Should().Contain(expectedHeader);
    }

    [Fact]
    public async Task InvokeAsync_ShouldContinue_WhenEndpointSkipsCorrelation()
    {
        var context = CreateContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new SkipClientCorrelationAttribute()), "test"));
        var called = false;

        await new ClientCorrelationMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ShouldContinue_WhenRequestIsWebSocketUpgrade()
    {
        var context = CreateContext();
        context.Request.Method = "GET";
        context.Request.Headers.Upgrade = "WebSocket";
        context.Request.Headers.Connection = "keep-alive, Upgrade";
        var called = false;

        await new ClientCorrelationMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
    }

    private static DefaultHttpContext CreateContext() => new()
    {
        RequestServices = new ServiceCollection().AddOptions().AddLogging().BuildServiceProvider(),
        Response = { Body = new MemoryStream() }
    };
}
