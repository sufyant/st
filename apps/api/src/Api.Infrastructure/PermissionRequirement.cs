using Microsoft.AspNetCore.Authorization;

namespace Api.Infrastructure;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
