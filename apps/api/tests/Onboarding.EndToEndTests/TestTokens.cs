using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Onboarding.EndToEndTests;

// Clerk is a system we do not own. The test signs session tokens shaped like Clerk's with a key of its own, and the host
// trusts that key in place of the keys it would fetch from Clerk; everything else about validation is the host's own.
internal static class TestTokens
{
    public const string Issuer = "https://clerk.test";

    public const string AuthorizedParty = "https://app.test";

    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test-key" };

    public static IServiceCollection TrustTestKey(this IServiceCollection services) =>
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            options.Configuration.SigningKeys.Add(Key);
        });

    // fva holds the minutes since the first and the second factor were verified; -1 means no second factor.
    public static string For(string userId, bool secondFactor)
    {
        var now = DateTime.UtcNow;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", userId)]),
            Claims = new Dictionary<string, object>
            {
                ["sid"] = "sess_test",
                ["v"] = 2,
                ["azp"] = AuthorizedParty,
                ["fva"] = new[] { 0, secondFactor ? 0 : -1 },
            },
            Issuer = Issuer,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
        });
    }
}
