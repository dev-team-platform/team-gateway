using AwesomeAssertions;
using Moq;
using RabbitMQ.Client;
using TeamGateway.Api.Services.RabbitMq;
using Xunit;

namespace TeamGateway.Api.Tests.Services.RabbitMq;

public class RabbitMqConnectionTests
{
    [Fact]
    public async Task GetConnectionAsync_ShouldReuseConnection_WhenConnectionIsOpen()
    {
        var connection = new Mock<IConnection>();
        connection.SetupGet(x => x.IsOpen).Returns(true);
        var factory = new Mock<IConnectionFactory>();
        factory.Setup(x => x.CreateConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(connection.Object);
        await using var service = new RabbitMqConnection(factory.Object);

        var first = await service.GetConnectionAsync();
        var second = await service.GetConnectionAsync();

        first.Should().BeSameAs(connection.Object);
        second.Should().BeSameAs(first);
        factory.Verify(x => x.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetConnectionAsync_ShouldReconnect_WhenPreviousConnectionIsClosed()
    {
        var closed = new Mock<IConnection>();
        closed.SetupGet(x => x.IsOpen).Returns(false);
        var open = new Mock<IConnection>();
        open.SetupGet(x => x.IsOpen).Returns(true);
        var factory = new Mock<IConnectionFactory>();
        factory.SetupSequence(x => x.CreateConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(closed.Object).ReturnsAsync(open.Object);
        await using var service = new RabbitMqConnection(factory.Object);

        await service.GetConnectionAsync();
        var result = await service.GetConnectionAsync();

        result.Should().BeSameAs(open.Object);
        closed.Verify(x => x.DisposeAsync(), Times.Once);
        factory.Verify(x => x.CreateConnectionAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetConnectionAsync_ShouldRejectRequests_WhenServiceIsDisposed()
    {
        var service = new RabbitMqConnection(new Mock<IConnectionFactory>().Object);
        await service.DisposeAsync();

        var action = () => service.GetConnectionAsync();

        await action.Should().ThrowAsync<ObjectDisposedException>();
    }
}
