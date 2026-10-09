namespace ControlPlane.Domain.SystemAdmins;

/// <summary>
/// A grant that makes a user a system admin: one of the provider's own staff. It is an extra grant on the single identity,
/// recorded with who granted it and when. The first one comes from configuration and has no granter (section 6).
/// </summary>
internal sealed class SystemAdmin(Guid userId, Guid? grantedBy, DateTimeOffset grantedAt)
{
    public Guid UserId { get; private init; } = userId;

    public Guid? GrantedBy { get; private init; } = grantedBy;

    public DateTimeOffset GrantedAt { get; private init; } = grantedAt;
}
