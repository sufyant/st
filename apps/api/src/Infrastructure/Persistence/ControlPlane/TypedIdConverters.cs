using Domain.ControlPlane.Administration;
using Domain.ControlPlane.Invitations;
using Domain.ControlPlane.Memberships;
using Domain.ControlPlane.Tenants;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Infrastructure.Persistence.ControlPlane;

public sealed class TenantIdConverter() : ValueConverter<TenantId, Guid>(
    id => id.Value,
    value => new TenantId(value));

public sealed class MembershipIdConverter() : ValueConverter<MembershipId, Guid>(
    id => id.Value,
    value => new MembershipId(value));

public sealed class InvitationIdConverter() : ValueConverter<InvitationId, Guid>(
    id => id.Value,
    value => new InvitationId(value));

public sealed class PlatformAdminIdConverter() : ValueConverter<PlatformAdminId, Guid>(
    id => id.Value,
    value => new PlatformAdminId(value));
