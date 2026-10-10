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
// transaction. Each step and each compensation may run twice (S7).
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
        var activate = started.Activate.ShouldNotBeNull().Message;
        var invitationId = await ScalarAsync<Guid>($"SELECT id FROM catalog.invitations WHERE tenant_id = '{tenantId}'");
        activate.ShouldSatisfyAllConditions(
            step => step.TenantId.ShouldBe(tenantId),
            step => step.InvitationId.ShouldBe(invitationId),
            step => step.Link.GetLeftPart(UriPartial.Path).ShouldBe(Database.AcceptUrl),
            step => Handlers.TenantIdOf(Handlers.CodeOf(step.Link)).ShouldBe(tenantId));
        started.Timeout.ShouldBe(new ActivationTimedOut(tenantId, TimeSpan.FromMinutes(10)));
        (await StatusOfAsync(tenantId)).ShouldBe("Provisioning");
        (await ScalarAsync<string>($"SELECT state FROM catalog.tenant_onboardings WHERE id = '{tenantId}' AND tenant_id = '{tenantId}'"))
            .ShouldBe("Activating");
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

        var secret = Handlers.SecretOf(Handlers.CodeOf(started.Activate.ShouldNotBeNull().Message.Link));
        (await ScalarAsync<string>($"SELECT token_hash FROM catalog.invitations WHERE tenant_id = '{tenantId}'")).ShouldBe(Sha256(secret));
    }

    // The steps it starts carry the saga's id on their envelopes, so the fault of one that fails for good finds the saga.
    [Fact]
    public async Task StartOnboarding_NewTenant_SendsTheActivationWithTheSagaId()
    {
        var admin = await Catalog.AddUserAsync(database.Services);
        var tenantId = Guid.CreateVersion7();

        var started = await Handlers.StartOnboardingAsync(database.Services, tenantId, admin.ExternalId, Unique.Slug(), Unique.Email());

        started.Activate.ShouldNotBeNull().Options.SagaId.ShouldBe(tenantId.ToString());
    }

    [Fact]
    public async Task StartOnboarding_SlugTaken_IsRefusedAndStartsNothing()
    {
        var admin = await Catalog.AddUserAsync(database.Services);
        var existing = await Catalog.AddTenantAsync(database.Services);

        var started = await Handlers.StartOnboardingAsync(database.Services, Guid.CreateVersion7(), admin.ExternalId, existing.Slug, Unique.Email());

        started.Result.Error.Code.ShouldBe("tenant.slug_taken");
        started.Onboarding.ShouldBeNull();
        started.Activate.ShouldBeNull();
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
    public async Task ActivateTenant_Provisioning_ActivatesItAndAnnouncesTheTenantAndTheInvitation()
    {
        var admin = await Catalog.AddUserAsync(database.Services);
        var tenantId = Guid.CreateVersion7();
        var owner = Unique.Email();
        var started = await Handlers.StartOnboardingAsync(database.Services, tenantId, admin.ExternalId, Unique.Slug(), owner);
        var invitationId = started.Activate.ShouldNotBeNull().Message.InvitationId;
        var link = AcceptLink();

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

        var cancelled = await Handlers.CancelTenantAsync(database.Services, tenantId, new CancelTenant(tenantId, invitationId, "activation_failed"));

        cancelled.ShouldBe(new TenantCancelled(tenantId));
        (await StatusOfAsync(tenantId)).ShouldBe("Cancelled");
        (await ScalarAsync<string>($"SELECT cancellation_reason FROM catalog.tenants WHERE id = '{tenantId}'")).ShouldBe("activation_failed");
        (await ScalarAsync<string>($"SELECT status FROM catalog.invitations WHERE id = '{invitationId}'")).ShouldBe("Cancelled");
    }

    [Fact]
    public async Task CancelTenant_Repeated_KeepsTheFirstReason()
    {
        var (tenantId, invitationId) = await StartAsync();
        await Handlers.CancelTenantAsync(database.Services, tenantId, new CancelTenant(tenantId, invitationId, "activation_failed"));

        var cancelled = await Handlers.CancelTenantAsync(database.Services, tenantId, new CancelTenant(tenantId, invitationId, "another_reason"));

        cancelled.ShouldBe(new TenantCancelled(tenantId));
        (await ScalarAsync<string>($"SELECT cancellation_reason FROM catalog.tenants WHERE id = '{tenantId}'")).ShouldBe("activation_failed");
    }

    // The activation is the pivot: an active tenant is never cancelled. The compensation changes nothing and reports nothing, so the
    // saga's cancellation timeout leaves the onboarding to a person.
    [Fact]
    public async Task CancelTenant_Active_ChangesNothingAndReportsNoCancellation()
    {
        var (tenantId, invitationId) = await StartAsync();
        await Handlers.ActivateAsync(database.Services, tenantId, new ActivateTenant(tenantId, invitationId, AcceptLink()));

        var cancelled = await Handlers.CancelTenantAsync(database.Services, tenantId, new CancelTenant(tenantId, invitationId, "activation_failed"));

        cancelled.ShouldBeNull();
        (await StatusOfAsync(tenantId)).ShouldBe("Active");
        (await ScalarAsync<string>($"SELECT cancellation_reason FROM catalog.tenants WHERE id = '{tenantId}'")).ShouldBeNull();
        (await ScalarAsync<string>($"SELECT status FROM catalog.invitations WHERE id = '{invitationId}'")).ShouldBe("Pending");
    }

    [Fact]
    public async Task ActivateTenant_Cancelled_AnnouncesNothing()
    {
        var (tenantId, invitationId) = await StartAsync();
        await Handlers.CancelTenantAsync(database.Services, tenantId, new CancelTenant(tenantId, invitationId, "activation_failed"));

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

    // S7: the owner accepted before the cancel arrived, and the acceptance stands.
    [Fact]
    public async Task CancelInvitation_AlreadyAccepted_ChangesNothing()
    {
        var owner = Unique.Email();
        var userId = Unique.ExternalId();
        database.Identity.AddAccount(userId, owner);
        var (tenantId, _, ready) = await Handlers.OnboardAsync(database.Services, owner);
        (await Handlers.AcceptAsync(database.Services, Handlers.CodeOf(ready.Link), userId)).IsSuccess.ShouldBeTrue();

        await Handlers.CancelInvitationAsync(database.Services, tenantId, new CancelInvitation(tenantId, ready.InvitationId));

        (await ScalarAsync<string>($"SELECT status FROM catalog.invitations WHERE id = '{ready.InvitationId}'")).ShouldBe("Accepted");
        (await ScalarAsync<long>($"SELECT count(*) FROM catalog.memberships WHERE tenant_id = '{tenantId}'")).ShouldBe(1);
    }

    private async Task<(Guid TenantId, Guid InvitationId)> StartAsync()
    {
        var admin = await Catalog.AddUserAsync(database.Services);
        var tenantId = Guid.CreateVersion7();
        var started = await Handlers.StartOnboardingAsync(database.Services, tenantId, admin.ExternalId, Unique.Slug(), Unique.Email());

        return (tenantId, started.Activate.ShouldNotBeNull().Message.InvitationId);
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
