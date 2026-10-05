using ControlPlane.Application.Invitations;
using ControlPlane.Application.Ports;
using ControlPlane.Domain.Roles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Tenancy;

namespace ControlPlane.IntegrationTests;

// A system job closes the invitations whose time has run out (0027, 0029). It runs outside any tenant, across all of them.
public sealed class InvitationExpiryTests(Database database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // Long before the other tests' invitations, so closing these leaves theirs alone.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2020, 1, 1, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Pending_invitations_past_their_expiry_are_closed_and_the_others_are_left_alone()
    {
        await using var services = database.BuildServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_time)));
        var tenant = await Catalog.AddTenantAsync(services);
        var owner = await Catalog.AddMemberAsync(services, tenant, BuiltInRoles.Owner);
        var expired = await InviteAsync(services, tenant.Id, owner.ExternalId);
        var accepted = await InviteAndAcceptAsync(services, tenant.Id, owner.ExternalId);
        _time.Advance(TimeSpan.FromDays(4));
        var current = await InviteAsync(services, tenant.Id, owner.ExternalId);
        _time.Advance(TimeSpan.FromDays(4));

        await CloseExpiredAsync(services);

        (await StatusOfAsync(expired)).ShouldBe("Expired");
        (await StatusOfAsync(accepted)).ShouldBe("Accepted");
        (await StatusOfAsync(current)).ShouldBe("Pending");
    }

    // Once the job has closed it, an invitation is still refused as expired, not as used.
    [Fact]
    public async Task A_closed_invitation_is_refused_as_expired()
    {
        await using var services = database.BuildServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_time)));
        var tenant = await Catalog.AddTenantAsync(services);
        var owner = await Catalog.AddMemberAsync(services, tenant, BuiltInRoles.Owner);
        var (email, invitee) = (Unique.Email(), Unique.ExternalId());
        database.Identity.AddAccount(invitee, email);
        await Handlers.InviteAndDeliverAsync(services, tenant.Id, owner.ExternalId, email, BuiltInRoles.Member.Id);
        _time.Advance(TimeSpan.FromDays(8));
        await CloseExpiredAsync(services);

        var accepted = await Handlers.AcceptAsync(services, Handlers.TokenOf(database.Sender.LinkSentTo(email)), invitee);

        accepted.Error.Code.ShouldBe("invitation.expired");
    }

    private static async Task<Guid> InviteAsync(IServiceProvider services, Guid tenantId, string actorId) =>
        (await Handlers.InviteAndDeliverAsync(services, tenantId, actorId, Unique.Email(), BuiltInRoles.Member.Id)).Id;

    private async Task<Guid> InviteAndAcceptAsync(IServiceProvider services, Guid tenantId, string actorId)
    {
        var (email, invitee) = (Unique.Email(), Unique.ExternalId());
        database.Identity.AddAccount(invitee, email);
        var invitation = await Handlers.InviteAndDeliverAsync(services, tenantId, actorId, email, BuiltInRoles.Member.Id);
        var token = Handlers.TokenOf(database.Sender.LinkSentTo(email));
        (await Handlers.AcceptAsync(services, token, invitee)).IsSuccess.ShouldBeTrue();

        return invitation.Id;
    }

    private static async Task CloseExpiredAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await CloseExpiredInvitationsHandler.HandleAsync(
            new CloseExpiredInvitations(),
            scope.ServiceProvider.GetRequiredService<IInvitationExpiry>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            Cancellation);
    }

    private async Task<string> StatusOfAsync(Guid invitationId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionStringFor(DatabaseRoles.Application));
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand($"SELECT status FROM catalog.invitations WHERE id = '{invitationId}'", connection);

        return (string)(await command.ExecuteScalarAsync(Cancellation))!;
    }
}
