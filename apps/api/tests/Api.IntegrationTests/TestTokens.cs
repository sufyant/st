using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Api.IntegrationTests;

// Clerk is a system we do not own. Tests sign session tokens shaped like Clerk's with a key of their own, and the host
// trusts that key in place of the keys it would fetch from Clerk; everything else about validation is the host's own.
internal static class TestTokens
{
    public const string Issuer = "https://clerk.test";

    public const string AuthorizedParty = "https://app.test";

    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test-key" };

    private static readonly RsaSecurityKey UntrustedKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    public static Dictionary<string, string?> Settings => new()
    {
        ["Clerk:Issuer"] = Issuer,
        ["Clerk:AuthorizedParties:0"] = AuthorizedParty,
    };

    // Configured rather than post-configured, so the handler uses this configuration instead of fetching Clerk's metadata.
    public static IServiceCollection TrustTestKey(this IServiceCollection services) =>
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            options.Configuration.SigningKeys.Add(Key);
        });

    // fva holds the minutes since the first and the second factor were verified; -1 means no second factor.
    public static string For(
        string userId,
        bool secondFactor = false,
        string issuer = Issuer,
        string? authorizedParty = AuthorizedParty,
        TimeSpan? expiresIn = null,
        bool trusted = true,
        IReadOnlyDictionary<string, object>? extraClaims = null)
    {
        var now = DateTime.UtcNow;
        var expires = now + (expiresIn ?? TimeSpan.FromMinutes(5));
        Dictionary<string, object> claims = new() { ["sid"] = "sess_test", ["v"] = 2, ["fva"] = new[] { 0, secondFactor ? 0 : -1 } };
        if (authorizedParty is not null)
        {
            claims["azp"] = authorizedParty;
        }

        foreach (var (type, value) in extraClaims ?? new Dictionary<string, object>())
        {
            claims[type] = value;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", userId)]),
            Claims = claims,
            Issuer = issuer,
            IssuedAt = expires < now ? expires.AddMinutes(-2) : now,
            NotBefore = expires < now ? expires.AddMinutes(-2) : now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(trusted ? Key : UntrustedKey, SecurityAlgorithms.RsaSha256),
        });
    }
}
