using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Api.Tests.Shared;

public static class TestJwtTokenFactory
{
    public static string CreateToken(
        string clerkUserId,
        TimeSpan? lifetime = null,
        SecurityKey? signingKey = null,
        string? issuer = null,
        IEnumerable<Claim>? extraClaims = null)
    {
        var handler = new JwtSecurityTokenHandler();
        var credentials = new SigningCredentials(
            signingKey ?? CustomWebApplicationFactory.SigningKey, SecurityAlgorithms.RsaSha256);

        var claims = new List<Claim> { new("sub", clerkUserId) };
        if (extraClaims is not null)
        {
            claims.AddRange(extraClaims);
        }

        var token = new JwtSecurityToken(
            issuer: issuer ?? CustomWebApplicationFactory.TestIssuer,
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(5)),
            signingCredentials: credentials);

        return handler.WriteToken(token);
    }
}
