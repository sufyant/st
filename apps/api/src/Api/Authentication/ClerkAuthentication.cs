using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Authentication;

// Clerk only authenticates. Its session tokens are JWTs signed with keys published by the instance's Frontend API, which
// is also their issuer. They carry no audience; the authorized party (azp) is checked instead, as Clerk recommends. A JWT template
// token is signed with the same keys and issuer, but it cannot carry the session's id (sid), which every session token carries, so
// a token without one is refused (API2).
internal static class ClerkAuthentication
{
    public const string Section = "Authentication:Clerk";

    // Set only by the host, when the token's fva shows a second factor verified in the session.
    public const string SecondFactorClaim = "second_factor_verified";

    public static WebApplicationBuilder AddClerkAuthentication(this WebApplicationBuilder builder)
    {
        // Without a list of authorized parties the API accepts a session token issued to any origin of the Clerk instance. That is
        // convenient in Development; anywhere else the application does not start without the list of the clients allowed to use it.
        builder.Services.AddOptions<ClerkAuthenticationOptions>()
            .BindConfiguration(Section)
            .Validate(clerk => Uri.TryCreate(clerk.Issuer, UriKind.Absolute, out _), $"{Section}:Issuer must be the Frontend API URL of the Clerk instance.")
            .Validate<IHostEnvironment>(
                (clerk, environment) => environment.IsDevelopment() || clerk.AuthorizedParties.Any(party => !string.IsNullOrWhiteSpace(party)),
                $"{Section}:AuthorizedParties must list the origins of the clients allowed to use the API outside Development.")
            .ValidateOnStart();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<ClerkAuthenticationOptions>>((options, clerk) =>
            {
                options.Authority = clerk.Value.Issuer;

                // API8: a 401 does not say why the token failed.
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = clerk.Value.Issuer,
                    ValidateIssuer = true,
                    ValidateAudience = false,
                    ValidateLifetime = true,

                    // Clerk's session tokens live a minute; its own SDKs allow five seconds of clock skew.
                    ClockSkew = TimeSpan.FromSeconds(5),
                };
                options.Events = new JwtBearerEvents { OnTokenValidated = CheckClerkClaimsAsync };
            });

        return builder;
    }

    /// <summary>Whether the session verified a second factor: a neutral fact the host hands on, not a rule (A6).</summary>
    public static bool HasVerifiedSecondFactor(this ClaimsPrincipal user) => user.HasClaim(claim => claim.Type == SecondFactorClaim);

    private static Task CheckClerkClaimsAsync(TokenValidatedContext context)
    {
        if (context.SecurityToken is not JsonWebToken token || !token.TryGetPayloadValue<string>("sid", out var session) || string.IsNullOrEmpty(session))
        {
            context.Fail("Not a Clerk session token.");
            return Task.CompletedTask;
        }

        // A claim of this name inside the token vouches for nothing, for example one added through a custom session claim; only
        // the reading of fva below may set it.
        foreach (var identity in context.Principal?.Identities ?? [])
        {
            foreach (var claimed in identity.FindAll(SecondFactorClaim).ToList())
            {
                identity.RemoveClaim(claimed);
            }
        }

        // A browser token names the origin it was issued to; only the configured clients may use the API. Tokens of clients
        // without an origin, such as mobile apps, carry none.
        var parties = context.HttpContext.RequestServices.GetRequiredService<IOptions<ClerkAuthenticationOptions>>().Value.AuthorizedParties;
        if (token.TryGetPayloadValue<string>("azp", out var party) && parties.Count > 0 && !parties.Contains(party, StringComparer.Ordinal))
        {
            context.Fail("The token was issued to a party that may not use the API.");
            return Task.CompletedTask;
        }

        // fva holds the minutes since the first and the second factor were verified; -1 means never.
        if (token.TryGetPayloadValue<int[]>("fva", out var verificationAge) && verificationAge is [_, >= 0])
        {
            context.Principal?.Identities.First().AddClaim(new(SecondFactorClaim, "true"));
        }

        return Task.CompletedTask;
    }
}

internal sealed class ClerkAuthenticationOptions
{
    /// <summary>The Frontend API URL of the Clerk instance: the tokens' issuer and the source of their signing keys.</summary>
    public string? Issuer { get; set; }

    /// <summary>The origins of the clients allowed to use the API (the tokens' <c>azp</c>).</summary>
    public List<string> AuthorizedParties { get; set; } = [];
}
