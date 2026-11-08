using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TeamGateway.Api.Options;
using TeamGateway.Api.Services.Auth;
using Xunit;

namespace TeamGateway.Api.Tests.Services.Auth;

public class InternalJwtIssuerTests
{
    [Fact]
    public void Create_ShouldSignTokenWithMappedAudienceAndSanitizedClaims_WhenPrincipalIsValid()
    {
        using var rsa = RSA.Create(2048);
        var path = WriteKey(rsa);
        try
        {
            var issuer = CreateIssuer(path);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("sub", "subject-123"), new Claim("name", "Alice"),
                new Claim("aud", "untrusted"), new Claim("iss", "untrusted"),
                new Claim("exp", "1"), new Claim("iat", "1")], "test"));

            var result = issuer.Create(principal, "organization");

            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var validated = handler.ValidateToken(result, new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = "gateway",
                ValidateAudience = true, ValidAudience = "organization-api",
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
                ValidateIssuerSigningKey = true, IssuerSigningKey = new RsaSecurityKey(rsa)
            }, out _);
            validated.FindFirst("sub")!.Value.Should().Be("subject-123");
            validated.FindFirst("name")!.Value.Should().Be("Alice");
            validated.FindAll("aud").Select(x => x.Value).Should().Equal("organization-api");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Create_ShouldRejectRequest_WhenAudienceMappingIsMissing()
    {
        using var rsa = RSA.Create(2048);
        var path = WriteKey(rsa);
        try
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user")], "test"));
            var action = () => CreateIssuer(path).Create(principal, "unknown");
            action.Should().Throw<UnauthorizedAccessException>();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Create_ShouldRejectRequest_WhenSubjectIsMissing()
    {
        using var rsa = RSA.Create(2048);
        var path = WriteKey(rsa);
        try
        {
            var action = () => CreateIssuer(path).Create(new ClaimsPrincipal(new ClaimsIdentity()), "organization");
            action.Should().Throw<UnauthorizedAccessException>();
        }
        finally { File.Delete(path); }
    }

    private static string WriteKey(RSA rsa)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gateway-test-{Guid.NewGuid():N}.pem");
        File.WriteAllText(path, rsa.ExportPkcs8PrivateKeyPem());
        return path;
    }

    private static InternalJwtIssuer CreateIssuer(string path) => new(Microsoft.Extensions.Options.Options.Create(new AuthOptions
    {
        InternalJwt = new InternalJwtOptions
        {
            Issuer = "gateway", PrivateKeyPemPath = path, Lifetime = TimeSpan.FromMinutes(5),
            AudienceMappings = new Dictionary<string, string> { ["organization"] = "organization-api" }
        }
    }));
}
