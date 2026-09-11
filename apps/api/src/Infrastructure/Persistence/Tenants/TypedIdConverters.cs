using Domain.Authorization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Infrastructure.Persistence.Tenants;

public sealed class UserIdConverter() : ValueConverter<UserId, Guid>(
    id => id.Value,
    value => new UserId(value));

public sealed class RoleIdConverter() : ValueConverter<RoleId, Guid>(
    id => id.Value,
    value => new RoleId(value));

public sealed class PermissionIdConverter() : ValueConverter<PermissionId, Guid>(
    id => id.Value,
    value => new PermissionId(value));
