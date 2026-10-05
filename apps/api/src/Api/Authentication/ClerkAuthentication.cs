using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Authentication;

// Clerk only authenticates (0028). Its session tokens are JWTs signed with keys published by the instance's Frontend API, which
// is also their issuer. They carry no audience; the authorized party (azp) is checked instead, as Clerk recommends.
internal static class ClerkAuthentication
{
    public const string Section = "Clerk";

    // Set only by the host, when the token's fva shows a second factor verified in the session (0031).
    public const string SecondFactorClaim = "second_factor_verified";

    public static WebApplicationBuilder AddClerkAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<ClerkAuthenticationOptions>().BindConfiguration(Section);
        builder.Services.AddHealthChecks().AddCheck<AuthorizedPartiesHealthCheck>("clerk-authorized-parties", tags: ["ready"]);

        // Checked on first use rather than on start, like the connection strings: the build starts the host without
        // configuration to write the OpenAPI document. Without an issuer no token validates, so every request stays anonymous.
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<ClerkAuthenticationOptions>>((options, clerk) =>
            {
                options.Authority = clerk.Value.Issuer;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = clerk.Value.Issuer,
                    ValidateIssuer = true,
                    ValidateAudience = false,
                    ValidateLifetime = true,

                    // Clerk's session tokens live a minute; its own SDKs allow five seconds of clock skew.
                    ClockSkew = TimeSpan.FromSeconds(5),
                };
                options.Events = new JwtBearerEvents { OnMessageReceived = ReadHubTokenFromQuery, OnTokenValidated = CheckClerkClaimsAsync };
            });

        return builder;
    }

    // A browser cannot send a header when it opens a WebSocket, so a SignalR hub takes the token from the query string (0037). Only
    // there: anywhere else a token in a URL would end up in logs and caches. Routing runs before authentication, so the endpoint
    // is known here. The request log records the path without its query string.
    private static Task ReadHubTokenFromQuery(MessageReceivedContext context)
    {
        if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<HubMetadata>() is not null
            && context.Request.Query["access_token"] is [{ Length: > 0 } token])
        {
            context.Token = token;
        }

        return Task.CompletedTask;
    }

    private static Task CheckClerkClaimsAsync(TokenValidatedContext context)
    {
        if (context.SecurityToken is not JsonWebToken token)
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

        // fva holds the minutes since the first and the second factor were verified; -1 means never (0031).
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
