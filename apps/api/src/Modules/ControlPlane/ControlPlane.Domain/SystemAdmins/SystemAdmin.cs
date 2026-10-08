namespace ControlPlane.Domain.SystemAdmins;

/// <summary>
/// A grant that makes a user a system admin: one of the provider's own staff (0031). It is an extra grant on the single identity,
/// recorded with who granted it and when; the first one is written by the seed script.
/// </summary>
internal sealed class SystemAdmin(Guid userId, Guid? grantedBy, DateTimeOffset grantedAt)
{
    public Guid UserId { get; private init; } = userId;

    public Guid? GrantedBy { get; private init; } = grantedBy;

    public DateTimeOffset GrantedAt { get; private init; } = grantedAt;
}
