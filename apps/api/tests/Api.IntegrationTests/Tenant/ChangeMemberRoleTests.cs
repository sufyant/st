using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Tenancy;

namespace Api.IntegrationTests.Tenant;

// PATCH /v1/tenants/{tenantId}/members/{userId}: an Owner or an Admin gives a member a built-in role. Only an Owner gives or takes the
// Owner role, and the last Owner keeps it (K1).
public sealed class ChangeMemberRoleTests(Database database) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly Catalog _catalog = new(database);
    private readonly AuditLogs _audit = new(database);
    private ApiFactory _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFactory(database.ConnectionStringFor(DatabaseRoles.Application));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    public async Task ChangeMemberRole_AMemberToAdmin_AnswersTheMemberAndRecordsTheOldAndTheNewRole(string actorRole)
    {
        var tenant = await _catalog.AddTenantAsync();
        var actor = await _catalog.AddMemberAsync(tenant.Id, role: actorRole);
        var member = await _catalog.AddMemberAsync(tenant.Id, await _catalog.AddUserAsync(email: "ada@example.com", firstName: "Ada", lastName: "Lovelace"));
        var memberId = await _catalog.UserIdOfAsync(member);

        var changed = await _api.WaitingForMessagesAsync(() => _api.CreateClient(actor).ChangeMemberRoleAsync(tenant.Id, memberId, "Admin"));

        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await changed.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        body.GetProperty("userId").GetGuid().ShouldBe(memberId);
        body.GetProperty("email").GetString().ShouldBe("ada@example.com");
        body.GetProperty("lastName").GetString().ShouldBe("Lovelace");
        body.GetProperty("role").GetString().ShouldBe("Admin");
        body.GetProperty("deleted").GetBoolean().ShouldBeFalse();
        (await RoleOfAsync(tenant.Id, memberId)).ShouldBe("Admin");
        (await _audit.TenantLogAsync(memberId))
            .ShouldBe($"{tenant.Id}:member.role_changed|User|{await _catalog.UserIdOfAsync(actor)}|User|{memberId}|-");
        (await _audit.TenantDetailsAsync(memberId)).ShouldBe("""{"newRole": "Admin", "oldRole": "Member"}""");
    }

    [Fact]
    public async Task ChangeMemberRole_AnAdminMakesAnAdminAMember_ChangesIt()
    {
        var tenant = await _catalog.AddTenantAsync();
        var admin = await _catalog.AddMemberAsync(tenant.Id, role: "Admin");
        var other = await _catalog.UserIdOfAsync(await _catalog.AddMemberAsync(tenant.Id, role: "Admin"));

        var changed = await _api.CreateClient(admin).ChangeMemberRoleAsync(tenant.Id, other, "Member");

        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RoleOfAsync(tenant.Id, other)).ShouldBe("Member");
    }

    [Fact]
    public async Task ChangeMemberRole_AnOwnerMakesAMemberAnOwner_ChangesIt()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var member = await _catalog.UserIdOfAsync(await _catalog.AddMemberAsync(tenant.Id));

        var changed = await _api.CreateClient(owner).ChangeMemberRoleAsync(tenant.Id, member, "Owner");

        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RoleOfAsync(tenant.Id, member)).ShouldBe("Owner");
    }

    // section 6, "Member management", check 4: an Admin does not touch an Owner, whatever role it would give.
    [Theory]
    [InlineData("Admin")]
    [InlineData("Member")]
    public async Task ChangeMemberRole_AnAdminOnAnOwner_IsForbiddenAndKeepsTheRole(string role)
    {
        var tenant = await _catalog.AddTenantAsync();
        await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var admin = await _catalog.AddMemberAsync(tenant.Id, role: "Admin");
        var owner = await _catalog.UserIdOfAsync(await _catalog.AddMemberAsync(tenant.Id, role: "Owner"));

        var changed = await _api.CreateClient(admin).ChangeMemberRoleAsync(tenant.Id, owner, role);

        changed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOfAsync(changed)).ShouldBe("membership.owner_requires_owner");
        (await RoleOfAsync(tenant.Id, owner)).ShouldBe("Owner");
    }

    [Fact]
    public async Task ChangeMemberRole_AnAdminGivesTheOwnerRole_IsForbidden()
    {
        var tenant = await _catalog.AddTenantAsync();
        var admin = await _catalog.AddMemberAsync(tenant.Id, role: "Admin");
        var member = await _catalog.UserIdOfAsync(await _catalog.AddMemberAsync(tenant.Id));

        var changed = await _api.CreateClient(admin).ChangeMemberRoleAsync(tenant.Id, member, "Owner");

        changed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOfAsync(changed)).ShouldBe("membership.owner_requires_owner");
        (await RoleOfAsync(tenant.Id, member)).ShouldBe("Member");
    }

    // K1: an Owner steps down when another Owner exists.
    [Fact]
    public async Task ChangeMemberRole_AnOwnerStepsDownWithAnotherOwner_ChangesIt()
    {
        var tenant = await _catalog.AddTenantAsync();
        await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var ownerId = await _catalog.UserIdOfAsync(owner);

        var changed = await _api.CreateClient(owner).ChangeMemberRoleAsync(tenant.Id, ownerId, "Admin");

        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RoleOfAsync(tenant.Id, ownerId)).ShouldBe("Admin");
    }

    // section 6, "Member management", check 5: the last Owner keeps the role.
    [Fact]
    public async Task ChangeMemberRole_TheLastOwnerStepsDown_IsAConflictAndKeepsTheRole()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var ownerId = await _catalog.UserIdOfAsync(owner);

        var changed = await _api.CreateClient(owner).ChangeMemberRoleAsync(tenant.Id, ownerId, "Admin");

        changed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(changed)).ShouldBe("membership.last_owner");
        (await RoleOfAsync(tenant.Id, ownerId)).ShouldBe("Owner");
    }

    // K1: an Owner whose user the identity provider deleted still counts, so the other Owner may step down.
    [Fact]
    public async Task ChangeMemberRole_TheOtherOwnerIsADeletedUser_CountsThatOwner()
    {
        var tenant = await _catalog.AddTenantAsync();
        var deleted = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        await _catalog.DeleteUserAsync(deleted);
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var ownerId = await _catalog.UserIdOfAsync(owner);

        var changed = await _api.CreateClient(owner).ChangeMemberRoleAsync(tenant.Id, ownerId, "Admin");

        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RoleOfAsync(tenant.Id, ownerId)).ShouldBe("Admin");
    }

    // K1: an Admin may become a Member, and cannot make themselves an Owner.
    [Fact]
    public async Task ChangeMemberRole_AnAdminBecomesAMember_ChangesIt()
    {
        var tenant = await _catalog.AddTenantAsync();
        var admin = await _catalog.AddMemberAsync(tenant.Id, role: "Admin");
        var adminId = await _catalog.UserIdOfAsync(admin);

        var changed = await _api.CreateClient(admin).ChangeMemberRoleAsync(tenant.Id, adminId, "Member");

        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RoleOfAsync(tenant.Id, adminId)).ShouldBe("Member");
    }

    [Fact]
    public async Task ChangeMemberRole_AnAdminMakesThemselvesAnOwner_IsForbidden()
    {
        var tenant = await _catalog.AddTenantAsync();
        var admin = await _catalog.AddMemberAsync(tenant.Id, role: "Admin");
        var adminId = await _catalog.UserIdOfAsync(admin);

        var changed = await _api.CreateClient(admin).ChangeMemberRoleAsync(tenant.Id, adminId, "Owner");

        changed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOfAsync(changed)).ShouldBe("membership.owner_requires_owner");
        (await RoleOfAsync(tenant.Id, adminId)).ShouldBe("Admin");
    }

    // K1 and I1: setting a state twice changes nothing, so the second request answers 200 and writes no second record.
    [Fact]
    public async Task ChangeMemberRole_TheSameRoleTwice_AnswersOkAndRecordsOnce()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = _api.CreateClient(await _catalog.AddMemberAsync(tenant.Id, role: "Owner"));
        var member = await _catalog.UserIdOfAsync(await _catalog.AddMemberAsync(tenant.Id));
        await _api.WaitingForMessagesAsync(() => owner.ChangeMemberRoleAsync(tenant.Id, member, "Admin"));

        var again = await _api.WaitingForMessagesAsync(() => owner.ChangeMemberRoleAsync(tenant.Id, member, "Admin"));

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("role").GetString().ShouldBe("Admin");
        (await _audit.TenantLogAsync(member)).ShouldNotBeNull().Split(", ").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ChangeMemberRole_UnknownUser_IsNotFound()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var changed = await _api.CreateClient(owner).ChangeMemberRoleAsync(tenant.Id, Guid.NewGuid(), "Admin");

        changed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(changed)).ShouldBe("membership.not_found");
    }

    // Row level security hides another tenant's member, so their id answers as an unknown one.
    [Fact]
    public async Task ChangeMemberRole_AnotherTenantsMember_IsNotFoundAndKeepsTheRole()
    {
        var tenant = await _catalog.AddTenantAsync();
        var other = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var stranger = await _catalog.UserIdOfAsync(await _catalog.AddMemberAsync(other.Id));

        var changed = await _api.CreateClient(owner).ChangeMemberRoleAsync(tenant.Id, stranger, "Admin");

        changed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(changed)).ShouldBe("membership.not_found");
        (await RoleOfAsync(other.Id, stranger)).ShouldBe("Member");
    }

    [Fact]
    public async Task ChangeMemberRole_AsAMember_IsForbidden()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id);
        var other = await _catalog.UserIdOfAsync(await _catalog.AddMemberAsync(tenant.Id));

        var changed = await _api.CreateClient(member).ChangeMemberRoleAsync(tenant.Id, other, "Admin");

        changed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RoleOfAsync(tenant.Id, other)).ShouldBe("Member");
    }

    // K6: the only two Owners demote each other at the same moment. The tenant row lock runs the two changes one after the other. The
    // second runs as an Admin, who may not touch an Owner (section 6, "Member management", check 4), so one Owner remains.
    [Fact]
    public async Task ChangeMemberRole_TheOnlyTwoOwnersDemoteEachOtherAtOnce_LeaveOneOwner()
    {
        var tenant = await _catalog.AddTenantAsync();
        var one = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var other = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var oneId = await _catalog.UserIdOfAsync(one);
        var otherId = await _catalog.UserIdOfAsync(other);

        var responses = await database.AtOnceAsync(
            tenant.Id,
            () => _api.CreateClient(one).ChangeMemberRoleAsync(tenant.Id, otherId, "Admin"),
            () => _api.CreateClient(other).ChangeMemberRoleAsync(tenant.Id, oneId, "Admin"));

        responses.Select(response => response.StatusCode).Order().ShouldBe([HttpStatusCode.OK, HttpStatusCode.Forbidden]);
        (await CodeOfAsync(responses.Single(response => response.StatusCode == HttpStatusCode.Forbidden))).ShouldBe("membership.owner_requires_owner");
        (await OwnersOfAsync(tenant.Id)).ShouldBe(1);
    }

    // Section 6, "Member management", check 1: an Owner removes another Owner while that Owner's role change waits for the tenant's lock.
    // The change then runs for a user who is no longer a member, to whom the tenant does not exist (T3).
    [Fact]
    public async Task ChangeMemberRole_ByAnOwnerRemovedWhileItWaited_IsNotFoundAndChangesNothing()
    {
        var tenant = await _catalog.AddTenantAsync();
        var remover = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var removed = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var removedId = await _catalog.UserIdOfAsync(removed);
        var member = await _catalog.UserIdOfAsync(await _catalog.AddMemberAsync(tenant.Id));

        var responses = await database.OneAfterTheOtherAsync(
            tenant.Id,
            () => _api.CreateClient(remover).RemoveMemberAsync(tenant.Id, removedId),
            () => _api.CreateClient(removed).ChangeMemberRoleAsync(tenant.Id, member, "Admin"));

        responses[0].StatusCode.ShouldBe(HttpStatusCode.NoContent);
        responses[1].StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(responses[1])).ShouldBe("tenant.not_found");
        (await RoleOfAsync(tenant.Id, member)).ShouldBe("Member");
    }

    // The role is required and is its exact name: a request without one, with an unknown one, with a number, with a list of names, with
    // spaces or in another case is refused, and never falls back to the enum's first value, Owner.
    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "role": "Root" }""")]
    [InlineData("""{ "role": 0 }""")]
    [InlineData("""{ "role": "Owner, Admin" }""")]
    [InlineData("""{ "role": "Admin, Member" }""")]
    [InlineData("""{ "role": " Admin" }""")]
    [InlineData("""{ "role": "admin" }""")]
    public async Task ChangeMemberRole_WithoutAValidRole_IsABadRequestAndKeepsTheRole(string body)
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var member = await _catalog.UserIdOfAsync(await _catalog.AddMemberAsync(tenant.Id));

        var changed = await _api.CreateClient(owner).PatchAsync(
            $"/v1/tenants/{tenant.Id}/members/{member}", new StringContent(body, Encoding.UTF8, "application/json"), Cancellation);

        changed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RoleOfAsync(tenant.Id, member)).ShouldBe("Member");
    }

    // K3: an entry holds ids and roles, never the member's name or email address.
    [Fact]
    public async Task ChangeMemberRole_AMemberWithAProfile_LeavesTheNameAndTheEmailOutOfTheAuditLog()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var member = await _catalog.UserIdOfAsync(
            await _catalog.AddMemberAsync(tenant.Id, await _catalog.AddUserAsync(email: "grace@example.com", firstName: "Grace", lastName: "Hopper")));

        await _api.WaitingForMessagesAsync(() => _api.CreateClient(owner).ChangeMemberRoleAsync(tenant.Id, member, "Admin"));

        (await _audit.TenantLogAsync(member)).ShouldNotBeNull();
        (await _audit.TenantRecordsHoldingAsync(tenant.Id, "@")).ShouldBe(0);
        (await _audit.TenantRecordsHoldingAsync(tenant.Id, "Grace")).ShouldBe(0);
        (await _audit.TenantRecordsHoldingAsync(tenant.Id, "Hopper")).ShouldBe(0);
    }

    private Task<string?> RoleOfAsync(Guid tenantId, Guid userId) =>
        database.ScalarAsSuperuserAsync<string>(
            $"""
            SELECT roles.built_in FROM control_plane.memberships JOIN control_plane.roles ON roles.id = memberships.role_id
            WHERE memberships.tenant_id = '{tenantId}' AND memberships.user_id = '{userId}'
            """);

    private Task<long> OwnersOfAsync(Guid tenantId) =>
        database.ScalarAsSuperuserAsync<long>(
            $"""
            SELECT count(*) FROM control_plane.memberships JOIN control_plane.roles ON roles.id = memberships.role_id
            WHERE memberships.tenant_id = '{tenantId}' AND roles.built_in = 'Owner'
            """);

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Cancellation))!.Extensions["code"]?.ToString();
}
