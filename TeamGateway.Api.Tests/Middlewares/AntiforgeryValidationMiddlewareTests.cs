using AwesomeAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TeamGateway.Api.Middlewares;
using Xunit;

namespace TeamGateway.Api.Tests.Middlewares;

public class AntiforgeryValidationMiddlewareTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public async Task InvokeAsync_ShouldSkipValidation_WhenMethodIsSafe(string method)
    {
        var context = CreateContext(method);
        var antiforgery = new Mock<IAntiforgery>();
        var called = false;

        await new AntiforgeryValidationMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context, antiforgery.Object);

        called.Should().BeTrue();
        antiforgery.Verify(x => x.ValidateRequestAsync(It.IsAny<HttpContext>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_ShouldContinue_WhenUnsafeRequestHasValidToken()
    {
        var context = CreateContext("POST");
        var antiforgery = new Mock<IAntiforgery>();
        antiforgery.Setup(x => x.ValidateRequestAsync(context)).Returns(Task.CompletedTask);
        var called = false;

        await new AntiforgeryValidationMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context, antiforgery.Object);

        called.Should().BeTrue();
        antiforgery.Verify(x => x.ValidateRequestAsync(context), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnBadRequest_WhenTokenIsInvalid()
    {
        var context = CreateContext("DELETE");
        var antiforgery = new Mock<IAntiforgery>();
        antiforgery.Setup(x => x.ValidateRequestAsync(context)).ThrowsAsync(new AntiforgeryValidationException("invalid"));
        var called = false;

        await new AntiforgeryValidationMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context, antiforgery.Object);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        called.Should().BeFalse();
    }

    private static DefaultHttpContext CreateContext(string method)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddOptions().AddLogging().BuildServiceProvider() };
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        return context;
    }
}
