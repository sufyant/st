using System.Net.Http.Json;
using ControlPlane.Application.Invitations;
using ControlPlane.Application.Members;
using ControlPlane.Application.Tenants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel;
using Tenancy;
using Wolverine;
using Wolverine.Tracking;

namespace Api.IntegrationTests;

// W7: Wolverine commits whatever a handler that returns normally has changed, a failed Result included. So each of ControlPlane's
// handlers that return a Result rejects before it changes anything: a rejected command leaves no row and sends no message.
public sealed class RejectionTests(Database database) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly Catalog _catalog = new(database);
    private ApiFactory _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFactory(database.ConnectionStringFor(DatabaseRoles.Application));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task StartTenantOnboarding_InvalidName_ChangesNoRowAndSendsNoMessage()
    {
        var admin = await _catalog.AddUserAsync();
        var tenantId = Guid.NewGuid();

        var (result, session) = await InvokeForTenantAsync<Result<TenantDetails>>(
            tenantId, new StartTenantOnboarding(admin, " ", Slug(), Email()));

        result.Error.Code.ShouldBe("tenant.name_invalid");
        (await _catalog.CountAsync($"SELECT count(*) FROM catalog.tenants WHERE id = '{tenantId}'")).ShouldBe(0);
        session.Sent.AllMessages().ShouldBeEmpty();
    }

    [Fact]
    public async Task StartTenantOnboarding_SlugTaken_ChangesNoRowAndSendsNoMessage()
    {
        var admin = await _catalog.AddUserAsync();
        var existing = await _catalog.AddTenantAsync();
        var tenantId = Guid.NewGuid();

        var (result, session) = await InvokeForTenantAsync<Result<TenantDetails>>(
            tenantId, new StartTenantOnboarding(admin, "Acme Ltd", existing.Slug, Email()));

        result.Error.Code.ShouldBe("tenant.slug_taken");
        (await _catalog.CountAsync($"SELECT count(*) FROM catalog.tenants WHERE id = '{tenantId}'")).ShouldBe(0);
        session.Sent.AllMessages().ShouldBeEmpty();
    }

    [Fact]
    public async Task AcceptInvitation_UnknownSecret_ChangesNoRowAndSendsNoMessage()
    {
        var tenant = await _catalog.AddTenantAsync();
        var user = $"user_{Guid.NewGuid():N}";

        var (result, session) = await InvokeForTenantAsync<Result<TenantSummary>>(
            tenant.Id, new AcceptInvitation("no-such-secret", user, [Email()]));

        result.Error.Code.ShouldBe("invitation.not_found");
        (await _catalog.CountAsync($"SELECT count(*) FROM catalog.users WHERE external_id = '{user}'")).ShouldBe(0);
        session.Sent.AllMessages().ShouldBeEmpty();
    }

    [Fact]
    public async Task AcceptInvitation_EmailNotVerified_ChangesNoRowAndSendsNoMessage()
    {
        var (tenantId, secret) = await InviteAsync(Email());
        var user = $"user_{Guid.NewGuid():N}";

        var (result, session) = await InvokeForTenantAsync<Result<TenantSummary>>(
            tenantId, new AcceptInvitation(secret, user, [Email()]));

        result.Error.Code.ShouldBe("invitation.email_mismatch");
        (await PendingInvitationsAsync(tenantId)).ShouldBe(1);
        (await _catalog.CountAsync($"SELECT count(*) FROM catalog.users WHERE external_id = '{user}'")).ShouldBe(0);
        session.Sent.AllMessages().ShouldBeEmpty();
    }

    [Fact]
    public async Task AcceptInvitation_AlreadyAMember_ChangesNoRowAndSendsNoMessage()
    {
        var email = Email();
        var (tenantId, secret) = await InviteAsync(email);
        var member = await _catalog.AddMemberAsync(tenantId);

        var (result, session) = await InvokeForTenantAsync<Result<TenantSummary>>(
            tenantId, new AcceptInvitation(secret, member, [email]));

        result.Error.Code.ShouldBe("membership.exists");
        (await PendingInvitationsAsync(tenantId)).ShouldBe(1);
        (await database.ScalarInTenantAsync<long>(tenantId, "SELECT count(*) FROM catalog.memberships")).ShouldBe(1);
        session.Sent.AllMessages().ShouldBeEmpty();
    }

    // A query has nothing to reject: the endpoint has checked the paging. It changes nothing and sends nothing either.
    [Fact]
    public async Task ListMembers_PageBeyondTheEnd_ChangesNoRowAndSendsNoMessage()
    {
        var tenant = await _catalog.AddTenantAsync();
        await _catalog.AddMemberAsync(tenant.Id);

        var (result, session) = await InvokeForTenantAsync<Result<ListPage<MemberSummary>>>(
            tenant.Id, new ListMembers(PageRequest.Create(2, 50).Value));

        result.Value.Items.ShouldBeEmpty();
        (await database.ScalarInTenantAsync<long>(tenant.Id, "SELECT count(*) FROM catalog.memberships")).ShouldBe(1);
        session.Sent.AllMessages().ShouldBeEmpty();
    }

    [Fact]
    public async Task ListMyTenants_UserTheCatalogDoesNotKnow_ChangesNoRowAndSendsNoMessage()
    {
        var user = $"user_{Guid.NewGuid():N}";
        var result = default(Result<ListPage<TenantSummary>>)!;

        var session = await _api.TrackMessagesAsync(async () =>
            result = await Bus().InvokeAsync<Result<ListPage<TenantSummary>>>(new ListMyTenants(user, PageRequest.Create(null, null).Value), Cancellation));

        result.Value.Items.ShouldBeEmpty();
        (await _catalog.CountAsync($"SELECT count(*) FROM catalog.users WHERE external_id = '{user}'")).ShouldBe(0);
        session.Sent.AllMessages().ShouldBeEmpty();
    }

    private IMessageBus Bus() => _api.Services.GetRequiredService<IHost>().MessageBus();

    private async Task<(T Result, ITrackedSession Session)> InvokeForTenantAsync<T>(Guid tenantId, object command)
    {
        var result = default(T)!;
        var session = await _api.TrackMessagesAsync(async () =>
            result = await Bus().InvokeForTenantAsync<T>(tenantId.ToString(), command, Cancellation));

        return (result, session);
    }

    // A tenant onboarded the way a system admin does it, with its first owner's invitation delivered; returns the invitation's secret.
    private async Task<(Guid TenantId, string Secret)> InviteAsync(string ownerEmail)
    {
        _api.Identity.AddAccount($"user_{Guid.NewGuid():N}", ownerEmail);
        var slug = Slug();
        var admin = _api.CreateClient(await _catalog.AddSystemAdminAsync(), secondFactor: true);
        await _api.WaitingForMessagesAsync(async () =>
            (await admin.PostAsJsonAsync("/v1/system/tenants", new { name = "Acme Ltd", slug, ownerEmail }, Cancellation)).EnsureSuccessStatusCode());
        var code = _api.Sender.CodeSentTo(ownerEmail);

        return (Guid.Parse(code[..code.IndexOf('.', StringComparison.Ordinal)]), code[(code.IndexOf('.', StringComparison.Ordinal) + 1)..]);
    }

    private Task<long> PendingInvitationsAsync(Guid tenantId) =>
        database.ScalarInTenantAsync<long>(tenantId, "SELECT count(*) FROM catalog.invitations WHERE status = 'Pending'");

    private static string Slug() => $"tenant-{Guid.NewGuid():N}"[..20];

    private static string Email() => $"{Guid.NewGuid():N}@example.com";
}
