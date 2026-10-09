using System.Security.Cryptography;
using System.Text;
using ControlPlane.Application.Ports;
using ControlPlane.Application.Tenants;
using ControlPlane.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tenancy;

namespace ControlPlane.IntegrationTests;

// The steps of the tenant onboarding saga, each run the way Wolverine runs it: a step that writes the catalog in the new tenant's
// transaction, a step that calls the identity provider without one. Each step and each compensation may run twice (S7).
public sealed class TenantOnboardingTests(Database database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartOnboarding_NewTenant_WritesTheTenantTheOwnersInvitationAndTheOnboarding()
    {
        var admin = await Catalog.AddUserAsync(database.Services);
        var tenantId = Guid.CreateVersion7();
        var slug = Unique.Slug();
        var owner = Unique.Email();

        var started = await Handlers.StartOnboardingAsync(database.Services, tenantId, admin.ExternalId, slug, owner);

        started.Result.Value.ShouldBe(new TenantDetails(tenantId, "Acme Ltd", slug, "Provisioning"));
        var register = started.Register.ShouldNotBeNull().Message;
        var invitationId = await ScalarAsync<Guid>($"SELECT id FROM catalog.invitations WHERE tenant_id = '{tenantId}'");
        register.ShouldSatisfyAllConditions(
            step => step.TenantId.ShouldBe(tenantId),
            step => step.InvitationId.ShouldBe(invitationId),
            step => step.Email.ShouldBe(owner),
            step => step.AcceptLink.GetLeftPart(UriPartial.Path).ShouldBe(Database.AcceptUrl),
            step => Handlers.TenantIdOf(Handlers.CodeOf(step.AcceptLink)).ShouldBe(tenantId));
        started.Timeout.ShouldBe(new RegistrationTimedOut(tenantId, new OnboardingSettings().RegistrationTimeout));
        (await StatusOfAsync(tenantId)).ShouldBe("Provisioning");
        (await ScalarAsync<string>($"SELECT state FROM catalog.tenant_onboardings WHERE id = '{tenantId}' AND tenant_id = '{tenantId}'"))
            .ShouldBe("Registering");
        (await ScalarAsync<long>(
            $"""
            SELECT count(*) FROM catalog.invitations JOIN catalog.roles ON roles.id = invitations.role_id
            WHERE invitations.id = '{invitationId}' AND invitations.email = '{owner}' AND invitations.status = 'Pending'
              AND roles.built_in = 'Owner' AND invitations.invited_by = '{admin.Id}'
            """)).ShouldBe(1);
    }

    // The secret is born with the invitation and travels only in the accept link; the catalog keeps its SHA-256 hash.
    [Fact]
    public async Task StartOnboarding_NewTenant_StoresOnlyTheHashOfTheSecretTheAcceptLinkCarries()
    {
        var admin = await Catalog.AddUserAsync(database.Services);
        var tenantId = Guid.CreateVersion7();

        var started = await Handlers.StartOnboardingAsync(database.Services, tenantId, admin.ExternalId, Unique.Slug(), Unique.Email());

        var secret = Handlers.SecretOf(Handlers.CodeOf(started.Register.ShouldNotBeNull().Message.AcceptLink));
        (await ScalarAsync<string>($"SELECT token_hash FROM catalog.invitations WHERE tenant_id = '{tenantId}'")).ShouldBe(Sha256(secret));
    }

    // The steps it starts carry the saga's id on their envelopes, so the fault of one that fails for good finds the saga.
    [Fact]
    public async Task StartOnboarding_NewTenant_SendsTheRegistrationWithTheSagaId()
    {
        var admin = await Catalog.AddUserAsync(database.Services);
        var tenantId = Guid.CreateVersion7();

        var started = await Handlers.StartOnboardingAsync(database.Services, tenantId, admin.ExternalId, Unique.Slug(), Unique.Email());

        started.Register.ShouldNotBeNull().Options.SagaId.ShouldBe(tenantId.ToString());
    }

    [Fact]
    public async Task StartOnboarding_SlugTaken_IsRefusedAndStartsNothing()
    {
        var admin = await Catalog.AddUserAsync(database.Services);
        var existing = await Catalog.AddTenantAsync(database.Services);

        var started = await Handlers.StartOnboardingAsync(database.Services, Guid.CreateVersion7(), admin.ExternalId, existing.Slug, Unique.Email());

        started.Result.Error.Code.ShouldBe("tenant.slug_taken");
        started.Onboarding.ShouldBeNull();
        started.Register.ShouldBeNull();
        started.Timeout.ShouldBeNull();
    }

    // Both find the slug free, and the slug's unique index lets only one of them have it: the other is a conflict, not a failure.
    [Fact]
    public async Task StartOnboarding_TwoWithOneSlugAtOnce_CreatesOneTenantAndRefusesTheOther()
    {
        var admin = await Catalog.AddUserAsync(database.Services);
        var slug = Unique.Slug();
        await using var firstScope = database.Services.CreateAsyncScope();
        await using var secondScope = database.Services.CreateAsyncScope();
        var (firstTransaction, _) = await BeginAsync(firstScope, Guid.CreateVersion7());
        var (_, secondProcessId) = await BeginAsync(secondScope, Guid.CreateVersion7());
        (await StartAsync(firstScope, admin.ExternalId, slug)).IsSuccess.ShouldBeTrue();

        var second = StartAsync(secondScope, admin.ExternalId, slug);
        await WaitUntilBlockedAsync(secondProcessId);
        await firstTransaction.CommitAsync(Cancellation);

        (await second).Error.Code.ShouldBe("tenant.slug_taken");
        (await ScalarAsync<long>($"SELECT count(*) FROM catalog.tenants WHERE slug = '{slug}'")).ShouldBe(1);
    }

    [Fact]
    public async Task RegisterOwner_WithoutAnAccount_InvitesThroughTheProviderToTheAcceptLink()
    {
        var step = new RegisterOwnerWithIdentityProvider(Guid.CreateVersion7(), Guid.CreateVersion7(), Unique.Email(), AcceptLink());

        var registered = await Handlers.RegisterOwnerAsync(database.Services, step);

        registered.ShouldBe(new OwnerRegistered(
            step.TenantId,
            step.InvitationId,
            new Uri($"https://clerk.test/invitations/{step.InvitationId}"),
            FakeIdentityProvider.ProviderInvitationIdOf(step.InvitationId)));
        database.Identity.Invitations.ShouldContain((step.Email, step.InvitationId, step.AcceptLink));
    }

    [Fact]
    public async Task RegisterOwner_WithAnAccount_SendsTheAcceptLinkAndCreatesNoProviderInvitation()
    {
        var step = new RegisterOwnerWithIdentityProvider(Guid.CreateVersion7(), Guid.CreateVersion7(), Unique.Email(), AcceptLink());
        database.Identity.AddAccount(Unique.ExternalId(), step.Email);

        var registered = await Handlers.RegisterOwnerAsync(database.Services, step);

        registered.ShouldBe(new OwnerRegistered(step.TenantId, step.InvitationId, step.AcceptLink, null));
        database.Identity.Invitations.ShouldNotContain(invited => invited.Email == step.Email);
    }

    [Fact]
    public async Task ActivateTenant_Provisioning_ActivatesItAndAnnouncesTheTenantAndTheInvitation()
    {
        var admin = await Catalog.AddUserAsync(database.Services);
        var tenantId = Guid.CreateVersion7();
        var owner = Unique.Email();
        var started = await Handlers.StartOnboardingAsync(database.Services, tenantId, admin.ExternalId, Unique.Slug(), owner);
        var invitationId = started.Register.ShouldNotBeNull().Message.InvitationId;
        var link = new Uri("https://clerk.test/invitations/the-link");

        var (activated, ready) = await Handlers.ActivateAsync(database.Services, tenantId, new ActivateTenant(tenantId, invitationId, link));

        (await StatusOfAsync(tenantId)).ShouldBe("Active");
        activated.ShouldNotBeNull().ShouldSatisfyAllConditions(
            announced => announced.TenantId.ShouldBe(tenantId),
            announced => announced.Name.ShouldBe("Acme Ltd"),
            announced => announced.CreatedBy.ShouldBe(admin.Id),
            announced => announced.EventId.ShouldNotBe(Guid.Empty));
        ready.ShouldNotBeNull().ShouldSatisfyAllConditions(
            invitation => invitation.TenantId.ShouldBe(tenantId),
            invitation => invitation.InvitationId.ShouldBe(invitationId),
            invitation => invitation.Email.ShouldBe(owner),
            invitation => invitation.TenantName.ShouldBe("Acme Ltd"),
            invitation => invitation.Link.ShouldBe(link),
            invitation => invitation.EventId.ShouldNotBe(activated.EventId));
    }

    [Fact]
    public async Task ActivateTenant_Repeated_AnnouncesNothingAgain()
    {
        var (tenantId, invitationId) = await StartAsync();
        var activate = new ActivateTenant(tenantId, invitationId, AcceptLink());
        await Handlers.ActivateAsync(database.Services, tenantId, activate);

        var (activated, ready) = await Handlers.ActivateAsync(database.Services, tenantId, activate);

        activated.ShouldBeNull();
        ready.ShouldBeNull();
    }

    [Fact]
    public async Task CancelTenant_Provisioning_CancelsTheTenantWithItsReasonAndTheInvitation()
    {
        var (tenantId, invitationId) = await StartAsync();

        var cancelled = await Handlers.CancelTenantAsync(database.Services, tenantId, new CancelTenant(tenantId, invitationId, "identity_provider_failed"));

        cancelled.ShouldBe(new TenantCancelled(tenantId));
        (await StatusOfAsync(tenantId)).ShouldBe("Cancelled");
        (await ScalarAsync<string>($"SELECT cancellation_reason FROM catalog.tenants WHERE id = '{tenantId}'")).ShouldBe("identity_provider_failed");
        (await ScalarAsync<string>($"SELECT status FROM catalog.invitations WHERE id = '{invitationId}'")).ShouldBe("Cancelled");
    }

    [Fact]
    public async Task CancelTenant_Repeated_KeepsTheFirstReason()
    {
        var (tenantId, invitationId) = await StartAsync();
        await Handlers.CancelTenantAsync(database.Services, tenantId, new CancelTenant(tenantId, invitationId, "identity_provider_failed"));

        var cancelled = await Handlers.CancelTenantAsync(database.Services, tenantId, new CancelTenant(tenantId, invitationId, "registration_timed_out"));

        cancelled.ShouldBe(new TenantCancelled(tenantId));
        (await ScalarAsync<string>($"SELECT cancellation_reason FROM catalog.tenants WHERE id = '{tenantId}'")).ShouldBe("identity_provider_failed");
    }

    // The activation is the pivot: an active tenant is never cancelled, so the compensation fails and the saga needs attention.
    [Fact]
    public async Task CancelTenant_Active_FailsAndLeavesTheTenantActive()
    {
        var (tenantId, invitationId) = await StartAsync();
        await Handlers.ActivateAsync(database.Services, tenantId, new ActivateTenant(tenantId, invitationId, AcceptLink()));

        var cancel = () => Handlers.CancelTenantAsync(database.Services, tenantId, new CancelTenant(tenantId, invitationId, "activation_failed"));

        await cancel.ShouldThrowAsync<InvalidOperationException>();
        (await StatusOfAsync(tenantId)).ShouldBe("Active");
    }

    [Fact]
    public async Task ActivateTenant_Cancelled_AnnouncesNothing()
    {
        var (tenantId, invitationId) = await StartAsync();
        await Handlers.CancelTenantAsync(database.Services, tenantId, new CancelTenant(tenantId, invitationId, "registration_timed_out"));

        var (activated, ready) = await Handlers.ActivateAsync(database.Services, tenantId, new ActivateTenant(tenantId, invitationId, AcceptLink()));

        activated.ShouldBeNull();
        ready.ShouldBeNull();
        (await StatusOfAsync(tenantId)).ShouldBe("Cancelled");
    }

    [Fact]
    public async Task CancelInvitation_Pending_CancelsItAndATenantStaysActive()
    {
        var (tenantId, invitationId) = await StartAsync();
        await Handlers.ActivateAsync(database.Services, tenantId, new ActivateTenant(tenantId, invitationId, AcceptLink()));

        await Handlers.CancelInvitationAsync(database.Services, tenantId, new CancelInvitation(tenantId, invitationId));
        await Handlers.CancelInvitationAsync(database.Services, tenantId, new CancelInvitation(tenantId, invitationId));

        (await ScalarAsync<string>($"SELECT status FROM catalog.invitations WHERE id = '{invitationId}'")).ShouldBe("Cancelled");
        (await StatusOfAsync(tenantId)).ShouldBe("Active");
    }

    [Fact]
    public async Task RevokeOwnerRegistration_WithAProviderInvitation_RevokesIt()
    {
        var providerInvitationId = $"inv_{Guid.NewGuid():N}";

        await Handlers.RevokeOwnerRegistrationAsync(database.Services, new RevokeOwnerRegistration(Guid.CreateVersion7(), providerInvitationId));

        database.Identity.Revoked.ShouldContain(providerInvitationId);
    }

    // The owner had an account, so the provider holds nothing of the onboarding.
    [Fact]
    public async Task RevokeOwnerRegistration_WithoutAProviderInvitation_DoesNothing()
    {
        var revoked = database.Identity.Revoked.Count;

        await Handlers.RevokeOwnerRegistrationAsync(database.Services, new RevokeOwnerRegistration(Guid.CreateVersion7(), null));

        database.Identity.Revoked.Count.ShouldBe(revoked);
    }

    private async Task<(Guid TenantId, Guid InvitationId)> StartAsync()
    {
        var admin = await Catalog.AddUserAsync(database.Services);
        var tenantId = Guid.CreateVersion7();
        var started = await Handlers.StartOnboardingAsync(database.Services, tenantId, admin.ExternalId, Unique.Slug(), Unique.Email());

        return (tenantId, started.Register.ShouldNotBeNull().Message.InvitationId);
    }

    private static Uri AcceptLink() => new($"{Database.AcceptUrl}?code={Guid.CreateVersion7()}.secret");

    // The transaction Wolverine would begin for the onboarding, and the database session it runs in.
    private static async Task<(IDbContextTransaction Transaction, int ProcessId)> BeginAsync(AsyncServiceScope scope, Guid tenantId)
    {
        var catalog = InTenant.Catalog(scope, tenantId);
        var transaction = await catalog.Database.BeginTransactionAsync(Cancellation);
        return (transaction, ((NpgsqlConnection)catalog.Database.GetDbConnection()).ProcessID);
    }

    private static async Task<SharedKernel.Result<TenantDetails>> StartAsync(AsyncServiceScope scope, string adminId, string slug) =>
        (await StartTenantOnboardingHandler.HandleAsync(
            new StartTenantOnboarding(adminId, "Acme Ltd", slug, Unique.Email(), Guid.NewGuid().ToString()),
            scope.ServiceProvider.GetRequiredService<ITenantCatalog>(),
            scope.ServiceProvider.GetRequiredService<Application.Invitations.InvitationSettings>(),
            scope.ServiceProvider.GetRequiredService<OnboardingSettings>(),
            TimeProvider.System,
            Cancellation)).Item1;

    // The second onboarding must be waiting on the first one's insert before the first commits, or the test would prove nothing.
    private async Task WaitUntilBlockedAsync(int processId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var connection = new NpgsqlConnection(database.ConnectionStringFor(DatabaseRoles.Application));
        await connection.OpenAsync(timeout.Token);
        await using var blocked = new NpgsqlCommand($"SELECT cardinality(pg_blocking_pids({processId})) > 0", connection);
        while (!(bool)(await blocked.ExecuteScalarAsync(timeout.Token))!)
        {
            timeout.Token.ThrowIfCancellationRequested();
        }
    }

    private Task<string> StatusOfAsync(Guid tenantId) => ScalarAsync<string>($"SELECT status FROM catalog.tenants WHERE id = '{tenantId}'");

    private static string Sha256(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private Task<T> ScalarAsync<T>(string sql) => database.ScalarAsSuperuserAsync<T>(sql);
}
