using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Api.Authorization;
using Application.Abstractions;
using Domain.Shared;

namespace Api.Http;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public ExternalUserId Id => ExternalUserId.Create(
        Principal.FindFirstValue("sub")
        ?? throw new InvalidOperationException("The principal carries no 'sub' claim."));

    private ClaimsPrincipal Principal => accessor.HttpContext?.User
                                         ?? throw new InvalidOperationException(
                                             "There is no request in scope.");

    public bool HasPermission(string code) =>
        Principal.HasClaim(TenantClaims.Permission, code);

    public bool TryGetEmail([MaybeNullWhen(false)] out EmailAddress email)
    {
        var value = Principal.FindFirstValue("email");

        if (string.IsNullOrWhiteSpace(value))
        {
            email = null;

            return false;
        }

        try
        {
            email = EmailAddress.Create(value);

            return true;
        }
        catch (ArgumentException)
        {
            email = null;

            return false;
        }
    }
}
