using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using ControlPlane.Application.Tenants;
using ControlPlane.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tenancy;

namespace ControlPlane.IntegrationTests;

// The steps of the tenant onboarding saga, each in the new tenant's transaction the way the outbox hands it on. The
// tenant's status is the saga's state.
public sealed class TenantOnboardingTests(Database database)
{
    [Fact]
    public async Task Starting_creates_the_tenant_as_provisioning_and_asks_for_its_first_owners_invitation()
    {
        var admin = await Catalog.AddUserAsync(database.Services);
        var tenantId = Guid.CreateVersion7();
        var slug = Unique.Slug();
        var owner = Unique.Email();

        var (tenant, next) = await Handlers.StartOnboardingAsync(database.Services, tenantId, admin.ExternalId, slug, owner);

        tenant.Value.ShouldBe(new TenantDetails(tenantId, "Acme Ltd", slug, "Provisioning"));
        next.ShouldNotBeNull().ShouldSatisfyAllConditions(
            step => step.OwnerEmail.ShouldBe(owner),
            step => step.InvitedBy.ShouldBe(admin.Id));
        (await StatusOfAsync(tenantId)).ShouldBe("Provisioning");
    }

    [Fact]
    public async Task A_slug_another_tenant_has_is_refused()
    {
        var admin = await Catalog.AddUserAsync(database.Services);
        var existing = await Catalog.AddTenantAsync(database.Services);

        var (tenant, next) = await Handlers.StartOnboardingAsync(database.Services, Guid.CreateVersion7(), admin.ExternalId, existing.Slug, Unique.Email());

        tenant.Error.Code.ShouldBe("tenant.slug_taken");
        next.ShouldBeNull();
    }

    // Both find the slug free, and the slug's unique index lets only one of them have it: the other is a conflict, not a failure.
    [Fact]
    public async Task Two_onboardings_of_one_slug_at_once_create_one_tenant_and_refuse_the_other()
    {
        var admin = await Catalog.AddUserAsync(database.Services);
        var slug = Unique.Slug();
        await using var firstScope = database.Services.CreateAsyncScope();
        await using var secondScope = database.Services.CreateAsyncScope();
        var firstTransaction = await BeginAsync(firstScope, Guid.CreateVersion7());
        var secondTransaction = await BeginAsync(secondScope, Guid.CreateVersion7());
        (await StartAsync(firstScope, admin.ExternalId, slug)).Tenant.IsSuccess.ShouldBeTrue();

        var second = StartAsync(secondScope, admin.ExternalId, slug);
        await WaitUntilBlockedAsync(secondTransaction.Connection.ProcessID);
        await firstTransaction.CommitAsync(TestContext.Current.CancellationToken);

        (await second).Tenant.Error.Code.ShouldBe("tenant.slug_taken");
        (await ScalarAsync<long>($"SELECT count(*) FROM catalog.tenants WHERE slug = '{slug}'")).ShouldBe(1);
    }

    [Fact]
    public async Task The_first_owner_is_invited_as_owner_and_given_no_token_until_the_invitation_is_delivered()
    {
        var (tenantId, invite) = await StartAsync();

        var next = await Handlers.InviteFirstOwnerAsync(database.Services, tenantId, invite);

        next.ShouldBe(new ActivateTenant(invite.InvitationId));
        (await ScalarAsync<long>(
            $"""
            SELECT count(*) FROM catalog.invitations JOIN catalog.roles ON roles.id = invitations.role_id
            WHERE invitations.id = '{invite.InvitationId}' AND invitations.email = '{invite.OwnerEmail}'
              AND roles.built_in = 'Owner' AND invitations.token_hash IS NULL AND invitations.invited_by = '{invite.InvitedBy}'
            """)).ShouldBe(1);
    }

    // A message may arrive twice.
    [Fact]
    public async Task A_repeated_invitation_step_invites_the_first_owner_once()
    {
        var (tenantId, invite) = await StartAsync();
        await Handlers.InviteFirstOwnerAsync(database.Services, tenantId, invite);

        await Handlers.InviteFirstOwnerAsync(database.Services, tenantId, invite);

        (await ScalarAsync<long>($"SELECT count(*) FROM catalog.invitations WHERE tenant_id = '{tenantId}'")).ShouldBe(1);
    }

    [Fact]
    public async Task Activating_makes_the_tenant_active_then_delivers_the_invitation_and_announces_the_tenant()
    {
        var (tenantId, invite) = await StartAsync();
        var activate = (await Handlers.InviteFirstOwnerAsync(database.Services, tenantId, invite)).ShouldNotBeNull();

        var (delivery, activated) = await Handlers.ActivateAsync(database.Services, tenantId, activate);

        delivery.ShouldBe(new DeliverInvitation(invite.InvitationId));
        activated.ShouldNotBeNull().TenantId.ShouldBe(tenantId);
        (await StatusOfAsync(tenantId)).ShouldBe("Active");
    }

    [Fact]
    public async Task A_repeated_activation_sends_nothing_again()
    {
        var (tenantId, invite) = await StartAsync();
        var activate = (await Handlers.InviteFirstOwnerAsync(database.Services, tenantId, invite)).ShouldNotBeNull();
        await Handlers.ActivateAsync(database.Services, tenantId, activate);

        var (delivery, activated) = await Handlers.ActivateAsync(database.Services, tenantId, activate);

        delivery.ShouldBeNull();
        activated.ShouldBeNull();
    }

    [Fact]
    public async Task The_compensation_leaves_the_tenant_failed()
    {
        var (tenantId, _) = await StartAsync();

        await Handlers.FailOnboardingAsync(database.Services, tenantId, new ActivateTenant(Guid.CreateVersion7()));

        (await StatusOfAsync(tenantId)).ShouldBe("Failed");
    }

    // A step that arrives after the compensation finds the onboarding over and does nothing.
    [Fact]
    public async Task A_failed_onboarding_goes_no_further()
    {
        var (tenantId, invite) = await StartAsync();
        await Handlers.FailOnboardingAsync(database.Services, tenantId, new ActivateTenant(invite.InvitationId));

        var next = await Handlers.InviteFirstOwnerAsync(database.Services, tenantId, invite);

        next.ShouldBeNull();
        (await ScalarAsync<long>($"SELECT count(*) FROM catalog.invitations WHERE tenant_id = '{tenantId}'")).ShouldBe(0);
    }

    private async Task<(Guid TenantId, CreateFirstOwnerInvitation Invite)> StartAsync()
    {
        var admin = await Catalog.AddUserAsync(database.Services);
        var tenantId = Guid.CreateVersion7();
        var (_, next) = await Handlers.StartOnboardingAsync(database.Services, tenantId, admin.ExternalId, Unique.Slug(), Unique.Email());

        return (tenantId, next.ShouldNotBeNull());
    }

    private static async Task<TenantTransaction> BeginAsync(AsyncServiceScope scope, Guid tenantId)
    {
        var transaction = scope.ServiceProvider.GetRequiredService<TenantTransaction>();
        await transaction.BeginAsync(tenantId, TestContext.Current.CancellationToken);
        return transaction;
    }

    private static async Task<(SharedKernel.Result<TenantDetails> Tenant, CreateFirstOwnerInvitation? Next)> StartAsync(
        AsyncServiceScope scope,
        string adminId,
        string slug) =>
        await StartTenantOnboardingHandler.HandleAsync(
            new StartTenantOnboarding(adminId, "Acme Ltd", slug, Unique.Email()),
            scope.ServiceProvider.GetRequiredService<ITenantCatalog>(),
            TimeProvider.System,
            TestContext.Current.CancellationToken);

    // The second onboarding must be waiting on the first one's insert before the first commits, or the test would prove nothing.
    private async Task WaitUntilBlockedAsync(int processId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
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

    private Task<T> ScalarAsync<T>(string sql) => database.ScalarAsSuperuserAsync<T>(sql);
}
