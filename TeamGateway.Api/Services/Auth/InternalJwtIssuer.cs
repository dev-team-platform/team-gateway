using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TeamGateway.Api.Options;

namespace TeamGateway.Api.Services.Auth;

public interface IInternalJwtIssuer
{
    string Create(ClaimsPrincipal principal, string clusterId);
}

public sealed class InternalJwtIssuer : IInternalJwtIssuer
{
    private static readonly HashSet<string> ReservedClaimTypes =
    [
        JwtRegisteredClaimNames.Aud,
        JwtRegisteredClaimNames.Exp,
        JwtRegisteredClaimNames.Iat,
        JwtRegisteredClaimNames.Iss
    ];

    private readonly IOptions<AuthOptions> _authOptions;
    private readonly SigningCredentials _credentials;

    public InternalJwtIssuer(IOptions<AuthOptions> authOptions)
    {
        _authOptions = authOptions;

        var keyPem = File.ReadAllText(_authOptions.Value.InternalJwt.PrivateKeyPemPath);
        var rsa = RSA.Create();
        rsa.ImportFromPem(keyPem);

        _credentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);
    }

    public string Create(ClaimsPrincipal principal, string clusterId)
    {
        if (!_authOptions.Value.InternalJwt.AudienceMappings.TryGetValue(clusterId, out var audience))
        {
            throw new UnauthorizedAccessException($"No audience mapping found for cluster ID '{clusterId}'.");
        }

        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new UnauthorizedAccessException("The authenticated principal has no sub claim.");

        var claims = principal.Claims
            .Where(claim => claim.Type != JwtRegisteredClaimNames.Sub
                && !ReservedClaimTypes.Contains(claim.Type))
            .Append(new Claim(JwtRegisteredClaimNames.Sub, subject))
            .ToList();

        var now = DateTimeOffset.UtcNow;
        var token = new JwtSecurityToken(
            issuer: _authOptions.Value.InternalJwt.Issuer,
            audience: audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.Add(_authOptions.Value.InternalJwt.Lifetime).UtcDateTime,
            signingCredentials: _credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
