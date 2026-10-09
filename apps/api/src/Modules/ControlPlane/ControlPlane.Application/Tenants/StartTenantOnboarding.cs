using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using ControlPlane.Domain.Invitations;
using ControlPlane.Domain.Roles;
using ControlPlane.Domain.Tenants;
using FluentValidation;
using SharedKernel;
using Wolverine;
using Wolverine.Persistence;

namespace ControlPlane.Application.Tenants;

/// <summary>
/// A system admin starts a tenant's onboarding. It runs inside the tenant it creates, whose id the caller chose, so every step
/// of the saga runs in that tenant's transaction and hands the tenant on to the next one. The idempotency key makes it safe to
/// send again: the same key with the same request answers with the tenant the first one created (section 8).
/// </summary>
public sealed record StartTenantOnboarding(string AdminId, string Name, string Slug, string OwnerEmail, string IdempotencyKey)
{
    /// <summary>Whether the client's value can be an idempotency key: 1 to 255 visible ASCII characters.</summary>
    public static bool IsIdempotencyKey(string? value) => TenantCreationRequest.IsKey(value);
}

public sealed record TenantDetails(Guid Id, string Name, string Slug, string Status);

public sealed class StartTenantOnboardingValidator : AbstractValidator<StartTenantOnboarding>
{
    public StartTenantOnboardingValidator()
    {
        // The tenant checks the name and the slug itself; a request without one never reaches it.
        RuleFor(command => command.Name).NotNull();
        RuleFor(command => command.Slug).NotNull();
        RuleFor(command => command.OwnerEmail).NotEmpty().EmailAddress().MaximumLength(Invitation.EmailMaxLength);
        RuleFor(command => command.IdempotencyKey).Must(TenantCreationRequest.IsKey);
    }
}

// Step 1 of the onboarding saga, in one transaction: the tenant as provisioning, its first owner's invitation, the saga's record
// (section 6) and the request under its idempotency key (section 8). The invitation's secret is born here; only its hash is stored,
// and the accept link that carries it goes on in the first step's message. Every rejection returns before the handler changes
// anything, because Wolverine commits a failed Result too (W7); a taken slug is found by the insert itself, which writes nothing then.
public static class StartTenantOnboardingHandler
{
    public static async Task<(Result<TenantDetails>, Insert<TenantOnboarding>?, DeliveryMessage<RegisterOwnerWithIdentityProvider>?, RegistrationTimedOut?)> HandleAsync(
        StartTenantOnboarding command,
        ITenantCatalog catalog,
        InvitationSettings invitations,
        OnboardingSettings onboarding,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var admin = await catalog.FindUserAsync(command.AdminId, cancellationToken)
            ?? throw new InvalidOperationException("A system admin is always a catalog user.");

        if (await catalog.FindCreationRequestAsync(admin.Id, command.IdempotencyKey, cancellationToken) is { } earlier)
        {
            return earlier.IsFor(command.Name, command.Slug, command.OwnerEmail)
                ? (Details(await catalog.FindTenantAsync(earlier.TenantId, cancellationToken)), null, null, null)
                : (Error.Unprocessable("idempotency_key_reused", "This idempotency key was used for another request."), null, null, null);
        }

        var tenant = Tenant.Create(catalog.TenantId, command.Name, command.Slug);
        if (!tenant.IsSuccess)
        {
            return (tenant.Error, null, null, null);
        }

        if (!await catalog.TryAddAsync(tenant.Value, cancellationToken))
        {
            return (Error.Conflict("tenant.slug_taken", "Another tenant already has this slug."), null, null, null);
        }

        var now = time.GetUtcNow();
        catalog.Add(TenantCreationRequest.Record(
            admin.Id, command.IdempotencyKey, command.Name, command.Slug, command.OwnerEmail, tenant.Value.Id, now));
        var secret = InvitationToken.Generate();
        var invitation = Invitation.Create(
            Guid.CreateVersion7(now), tenant.Value.Id, command.OwnerEmail, BuiltInRoles.Owner, admin.Id, now, invitations.Lifetime, secret);
        catalog.Add(invitation);

        var (saga, register, timeout) = TenantOnboarding.Begin(
            tenant.Value.Id,
            invitation.Id,
            invitation.Email,
            invitations.AcceptLink(new InvitationCode(tenant.Value.Id, secret)),
            onboarding.Timeouts);

        // A saga that is only being started gives its messages no saga id of their own; this one needs it, so that its fault finds
        // the saga.
        return (
            Details(tenant.Value),
            Storage.Insert(saga),
            register.WithDeliveryOptions(new DeliveryOptions { SagaId = saga.Id.ToString() }),
            timeout);
    }

    private static TenantDetails Details(Tenant tenant) => new(tenant.Id, tenant.Name, tenant.Slug, tenant.Status.ToString());
}
