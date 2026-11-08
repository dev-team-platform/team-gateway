using AwesomeAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using TeamGateway.Api.Constants;
using TeamGateway.Api.Controllers.V1;
using TeamGateway.Api.Options;
using Xunit;
using AntiforgeryOptions = TeamGateway.Api.Options.AntiforgeryOptions;

namespace TeamGateway.Api.Tests.Controllers;

public class AuthenticationControllerTests
{
    [Fact]
    public async Task Login_ShouldChallengeKeycloak_WhenReturnOriginIsAllowed()
    {
        var controller = CreateController();

        var result = (await controller.Login("https://example.test/home")).Should().BeOfType<ChallengeResult>().Which;

        result.AuthenticationSchemes.Should().ContainSingle().Which.Should().Be(AuthenticationSchemes.Keycloak);
        result.Properties!.RedirectUri.Should().Be("https://example.test/home");
    }

    [Theory]
    [InlineData("https://attacker.test/home")]
    [InlineData("/relative")]
    [InlineData("https://example.test.attacker.test/")]
    public async Task Login_ShouldRejectReturnUrl_WhenOriginIsNotAllowed(string returnUrl)
    {
        var action = () => CreateController().Login(returnUrl);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    private static AuthenticationController CreateController()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:AllowRedirectOrigins:0"] = "https://example.test"
        }).Build();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(Environments.Production);
        return new AuthenticationController(configuration, Mock.Of<IAntiforgery>(),
            Microsoft.Extensions.Options.Options.Create(new AntiforgeryOptions()), Mock.Of<IOptionsMonitor<OpenIdConnectOptions>>(),
            Microsoft.Extensions.Options.Options.Create(new AuthOptions()), environment.Object);
    }
}
