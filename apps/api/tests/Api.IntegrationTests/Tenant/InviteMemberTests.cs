using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using SharedKernel;
using Tenancy;
using Wolverine;

namespace Api.IntegrationTests.Tenant;

// POST /v1/tenants/{tenantId}/invitations: an Owner or an Admin invites someone to the tenant by email (K1, K2). The daily limit is 2
// here, so a test reaches it with three invitations.
public sealed class InviteMemberTests(Database database) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private const int DailyLimit = 2;

    private readonly Catalog _catalog = new(database);
    private readonly AuditLogs _audit = new(database);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero));
    private ApiFactory _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFactory(
            database.ConnectionStringFor(DatabaseRoles.Application),
            time: _time,
            settings: new Dictionary<string, string?> { ["ControlPlane:Invitations:DailyLimitPerTenant"] = $"{DailyLimit}" });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    public async Task InviteMember_AsAnOwnerOrAnAdmin_CreatesAPendingInvitation(string inviterRole)
    {
        var tenant = await _catalog.AddTenantAsync();
        var inviter = await _catalog.AddMemberAsync(tenant.Id, role: inviterRole);
        var email = NewEmail();

        var invited = await _api.CreateClient(inviter).InviteMemberAsync(tenant.Id, email);

        invited.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invitation = await invited.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        invitation.EnumerateObject().Select(field => field.Name).ShouldBe(["id", "email", "role", "invitedBy", "createdAt", "expiresAt"], ignoreOrder: true);
        invitation.GetProperty("email").GetString().ShouldBe(email);
        invitation.GetProperty("role").GetString().ShouldBe("Member");
        invitation.GetProperty("invitedBy").GetGuid().ShouldBe(await _catalog.UserIdOfAsync(inviter));
        invitation.GetProperty("createdAt").GetDateTimeOffset().ShouldBe(_time.GetUtcNow());
        invitation.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(_time.GetUtcNow().AddDays(7));
        (await _catalog.InvitationStatusOfAsync(invitation.GetProperty("id").GetGuid())).ShouldBe("Pending");
    }

    // Decision 5 of step 20j: without a language, the invitation speaks the inviter's own, and its email does too.
    [Fact]
    public async Task InviteMember_WithoutALanguage_InvitesInTheInvitersLanguage()
    {
        var tenant = await _catalog.AddTenantAsync();
        var inviter = await _catalog.AddMemberAsync(tenant.Id, await _catalog.AddUserAsync(language: "tr"), role: "Owner");
        var email = NewEmail();

        var invited = await _api.WaitingForMessagesAsync(() => _api.CreateClient(inviter).InviteMemberAsync(tenant.Id, email));

        invited.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await _catalog.InvitationLanguageOfAsync(email)).ShouldBe("tr");
        _api.Email.Sent.Single(sent => sent.To == email).Subject.ShouldBe("Acme Ltd adlı kuruluşa davet edildiniz");
    }

    [Fact]
    public async Task InviteMember_WithALanguage_InvitesInIt()
    {
        var tenant = await _catalog.AddTenantAsync();
        var inviter = await _catalog.AddMemberAsync(tenant.Id, await _catalog.AddUserAsync(language: "tr"), role: "Owner");
        var email = NewEmail();

        var invited = await _api.WaitingForMessagesAsync(() => _api.CreateClient(inviter).InviteMemberAsync(tenant.Id, email, language: "ar"));

        invited.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await _catalog.InvitationLanguageOfAsync(email)).ShouldBe("ar");
        _api.Email.Sent.Single(sent => sent.To == email).Subject.ShouldBe("دعوة للانضمام إلى Acme Ltd");
    }

    // The language is one of the list, by its tag, as in PATCH /v1/me/preferences.
    [Fact]
    public async Task InviteMember_WithALanguageNotOfTheList_IsABadRequestAndInvitesNobody()
    {
        var tenant = await _catalog.AddTenantAsync();
        var inviter = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var invited = await _api.CreateClient(inviter).InviteMemberAsync(tenant.Id, NewEmail(), language: "fr");

        invited.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await InvitationsOfAsync(tenant.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task InviteMember_AsAMember_IsForbidden()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id, role: "Member");

        var invited = await _api.CreateClient(member).InviteMemberAsync(tenant.Id, NewEmail());

        invited.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await InvitationsOfAsync(tenant.Id)).ShouldBe(0);
    }

    // K1: only an Owner gives the Owner role.
    [Fact]
    public async Task InviteMember_AnAdminForTheOwnerRole_IsForbidden()
    {
        var tenant = await _catalog.AddTenantAsync();
        var admin = await _catalog.AddMemberAsync(tenant.Id, role: "Admin");

        var invited = await _api.CreateClient(admin).InviteMemberAsync(tenant.Id, NewEmail(), role: "Owner");

        invited.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOfAsync(invited)).ShouldBe("invitation.owner_requires_owner");
        (await InvitationsOfAsync(tenant.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task InviteMember_AnOwnerForTheOwnerRole_CreatesTheInvitation()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var invited = await _api.CreateClient(owner).InviteMemberAsync(tenant.Id, NewEmail(), role: "Owner");

        invited.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await invited.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("role").GetString().ShouldBe("Owner");
    }

    [Theory]
    [InlineData("not-an-email", "Member")]
    [InlineData("", "Member")]
    [InlineData("   ", "Member")]
    public async Task InviteMember_AnEmailThatIsNoEmailAddress_IsABadRequest(string email, string role)
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var invited = await _api.CreateClient(owner).InviteMemberAsync(tenant.Id, email, role);

        invited.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await InvitationsOfAsync(tenant.Id)).ShouldBe(0);
    }

    [Theory]
    [InlineData("""{ "email": "ada@example.com" }""")]
    [InlineData("""{ "email": "ada@example.com", "role": "Guest" }""")]
    [InlineData("""{ "email": "ada@example.com", "role": 7 }""")]
    [InlineData("""{ "email": "ada@example.com", "role": 0 }""")]
    [InlineData("""{ "email": "ada@example.com", "role": "Owner, Admin" }""")]
    [InlineData("""{ "email": "ada@example.com", "role": "Admin, Member" }""")]
    [InlineData("""{ "email": "ada@example.com", "role": " Admin" }""")]
    [InlineData("""{ "email": "ada@example.com", "role": "admin" }""")]
    public async Task InviteMember_WithoutAKnownRole_IsABadRequest(string body)
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/tenants/{tenant.Id}/invitations")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var invited = await _api.CreateClient(owner).SendAsync(request, Cancellation);

        invited.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await InvitationsOfAsync(tenant.Id)).ShouldBe(0);
    }

    // The address is trimmed, as every invitation's is.
    [Fact]
    public async Task InviteMember_AnEmailWithSpacesAround_IsStoredTrimmed()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var email = NewEmail();

        var invited = await _api.CreateClient(owner).InviteMemberAsync(tenant.Id, $"  {email} ");

        (await invited.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("email").GetString().ShouldBe(email);
    }

    // K2: a new invitation to an address with a pending one replaces it. The old link stops working at once.
    [Fact]
    public async Task InviteMember_TheSameEmailAgain_CancelsTheFirstAndOnlyTheSecondLinkWorks()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = _api.CreateClient(await _catalog.AddMemberAsync(tenant.Id, role: "Owner"));
        var (invitee, email) = InviteeWithAnAccount();
        var first = await IdOfAsync(await _api.WaitingForMessagesAsync(() => owner.InviteMemberAsync(tenant.Id, email)));
        var second = await IdOfAsync(await _api.WaitingForMessagesAsync(() => owner.InviteMemberAsync(tenant.Id, email)));

        var withTheFirstLink = await AcceptAsync(invitee, _api.Email.CodeSentFor(first));
        var withTheSecondLink = await AcceptAsync(invitee, _api.Email.CodeSentFor(second));

        (await _catalog.InvitationStatusOfAsync(first)).ShouldBe("Cancelled");
        withTheFirstLink.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        withTheSecondLink.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // K3: a replacement is one command and one record, which names the invitation it replaced.
    [Fact]
    public async Task InviteMember_TheSameEmailAgain_RecordsOneMemberInvitedEntryForEachCommand()
    {
        var tenant = await _catalog.AddTenantAsync();
        var inviter = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var owner = _api.CreateClient(inviter);
        var inviterId = await _catalog.UserIdOfAsync(inviter);
        var email = NewEmail();
        var first = await IdOfAsync(await _api.WaitingForMessagesAsync(() => owner.InviteMemberAsync(tenant.Id, email)));

        var second = await IdOfAsync(await _api.WaitingForMessagesAsync(() => owner.InviteMemberAsync(tenant.Id, email, role: "Admin")));

        (await _audit.TenantLogAsync(first)).ShouldBe($"{tenant.Id}:member.invited|User|{inviterId}|Invitation|{first}|-");
        (await _audit.TenantLogAsync(second)).ShouldBe($"{tenant.Id}:member.invited|User|{inviterId}|Invitation|{second}|-");
        Details(await _audit.TenantDetailsAsync(first)).ShouldBe(("Member", null));
        Details(await _audit.TenantDetailsAsync(second)).ShouldBe(("Admin", first.ToString()));
        (await _audit.PlatformLogAsync(second)).ShouldBeNull();
    }

    // The same address in another case is the same address: the second invitation replaces the first.
    [Fact]
    public async Task InviteMember_TheSameEmailInAnotherCase_ReplacesThePendingInvitation()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = _api.CreateClient(await _catalog.AddMemberAsync(tenant.Id, role: "Owner"));
        var local = Guid.NewGuid().ToString("N");
        var first = await IdOfAsync(await owner.InviteMemberAsync(tenant.Id, $"Ada.{local}@Example.com"));

        var second = await IdOfAsync(await owner.InviteMemberAsync(tenant.Id, $"ada.{local}@example.COM"));

        (await _catalog.InvitationStatusOfAsync(first)).ShouldBe("Cancelled");
        (await _catalog.InvitationStatusOfAsync(second)).ShouldBe("Pending");
    }

    // K2, K6: a unique index keeps one pending invitation for each tenant and address, in any case. The lock is the first guard; the
    // index is the second.
    [Fact]
    public async Task StoreInvitation_ASecondPendingOneToTheSameEmailInAnotherCase_IsRefusedByTheIndex()
    {
        var tenant = await _catalog.AddTenantAsync();
        var local = Guid.NewGuid().ToString("N");
        await _catalog.AddInvitationAsync(tenant.Id, $"ada.{local}@example.com");

        var second = () => _catalog.AddInvitationAsync(tenant.Id, $"ADA.{local}@example.com");

        (await second.ShouldThrowAsync<PostgresException>()).SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    // The index covers only pending invitations, so an address may have many cancelled or accepted ones.
    [Fact]
    public async Task StoreInvitation_APendingOneBesideACancelledAndAnAcceptedOne_IsStored()
    {
        var tenant = await _catalog.AddTenantAsync();
        var email = NewEmail();
        await _catalog.AddInvitationAsync(tenant.Id, email, status: "Cancelled");
        await _catalog.AddInvitationAsync(tenant.Id, email, status: "Accepted");

        var pending = await _catalog.AddInvitationAsync(tenant.Id, email);

        (await _catalog.InvitationStatusOfAsync(pending)).ShouldBe("Pending");
    }

    // Row level security keeps the check to the tenant's own members: a member of another tenant with the address is no member here.
    [Fact]
    public async Task InviteMember_TheEmailOfAMemberOfAnotherTenant_CreatesTheInvitation()
    {
        var tenant = await _catalog.AddTenantAsync();
        var other = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var email = NewEmail();
        await _catalog.AddMemberAsync(other.Id, await _catalog.AddUserAsync(email: email));

        var invited = await _api.CreateClient(owner).InviteMemberAsync(tenant.Id, email);

        invited.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // The limit counts the tenant's own invitations only.
    [Fact]
    public async Task InviteMember_AnotherTenantAtItsLimit_CreatesTheInvitation()
    {
        var tenant = await _catalog.AddTenantAsync();
        var other = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var otherOwner = _api.CreateClient(await _catalog.AddMemberAsync(other.Id, role: "Owner"));
        (await otherOwner.InviteMemberAsync(other.Id, NewEmail())).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await otherOwner.InviteMemberAsync(other.Id, NewEmail())).StatusCode.ShouldBe(HttpStatusCode.Created);

        var invited = await _api.CreateClient(owner).InviteMemberAsync(tenant.Id, NewEmail());

        invited.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // The catalog's copy of a member's address, compared without case.
    [Fact]
    public async Task InviteMember_TheEmailOfAMember_IsAConflict()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var local = Guid.NewGuid().ToString("N");
        await _catalog.AddMemberAsync(tenant.Id, await _catalog.AddUserAsync(email: $"Grace.{local}@Navy.mil"));

        var invited = await _api.CreateClient(owner).InviteMemberAsync(tenant.Id, $"grace.{local}@navy.MIL");

        invited.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(invited)).ShouldBe("membership.exists");
        (await InvitationsOfAsync(tenant.Id)).ShouldBe(0);
    }

    // K2: the limit counts every invitation of the last 24 hours, replaced ones included, so a replacement does not get around it.
    [Fact]
    public async Task InviteMember_OverTheDailyLimit_IsTooManyRequestsUntil24HoursHavePassed()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = _api.CreateClient(await _catalog.AddMemberAsync(tenant.Id, role: "Owner"));
        var email = NewEmail();
        (await owner.InviteMemberAsync(tenant.Id, email)).StatusCode.ShouldBe(HttpStatusCode.Created);
        _time.Advance(TimeSpan.FromHours(1));
        (await owner.InviteMemberAsync(tenant.Id, email)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var overTheLimit = await owner.InviteMemberAsync(tenant.Id, NewEmail());
        _time.Advance(TimeSpan.FromHours(23));
        var aDayAfterTheFirst = await owner.InviteMemberAsync(tenant.Id, NewEmail());

        overTheLimit.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await CodeOfAsync(overTheLimit)).ShouldBe("invitation.daily_limit_reached");
        overTheLimit.Headers.RetryAfter.ShouldBeNull();
        aDayAfterTheFirst.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // K6: the second request waits for the first's lock on the tenant row, then replaces the invitation the first created.
    [Fact]
    public async Task InviteMember_TwoAtOnceToTheSameEmail_EndWithOnePendingInvitation()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = _api.CreateClient(await _catalog.AddMemberAsync(tenant.Id, role: "Owner"));
        var email = NewEmail();

        var statuses = await WhileTheTenantIsHeldAsync(tenant.Id, () => owner.InviteMemberAsync(tenant.Id, email), () => owner.InviteMemberAsync(tenant.Id, email));

        statuses.ShouldBe([HttpStatusCode.Created, HttpStatusCode.Created]);
        (await PendingInvitationsOfAsync(tenant.Id)).ShouldBe(1);
        (await InvitationsOfAsync(tenant.Id)).ShouldBe(2);
    }

    // K6: the limit is checked under the tenant's lock, so two requests at once cannot both take the last place.
    [Fact]
    public async Task InviteMember_TwoAtOnceWithOnePlaceLeft_NeverPassTheLimit()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = _api.CreateClient(await _catalog.AddMemberAsync(tenant.Id, role: "Owner"));
        // The first invitation's sign-up locks the tenant row too, after the commit; it ends before the holder counts its waiters.
        (await _api.WaitingForMessagesAsync(() => owner.InviteMemberAsync(tenant.Id, NewEmail()))).StatusCode.ShouldBe(HttpStatusCode.Created);

        var statuses = await WhileTheTenantIsHeldAsync(
            tenant.Id, () => owner.InviteMemberAsync(tenant.Id, NewEmail()), () => owner.InviteMemberAsync(tenant.Id, NewEmail()));

        statuses.Order().ShouldBe([HttpStatusCode.Created, HttpStatusCode.TooManyRequests]);
        (await InvitationsOfAsync(tenant.Id)).ShouldBe(DailyLimit);
    }

    // K6, T3: an Owner removes an Admin while the Admin's invitation waits for the tenant's lock. The invitation then runs for a user who
    // is no longer a member, to whom the tenant does not exist, and creates nothing.
    [Fact]
    public async Task InviteMember_ByAnAdminRemovedWhileItWaited_IsNotFoundAndCreatesNothing()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");
        var admin = await _catalog.AddMemberAsync(tenant.Id, role: "Admin");
        var adminId = await _catalog.UserIdOfAsync(admin);

        var responses = await database.OneAfterTheOtherAsync(
            tenant.Id,
            () => _api.CreateClient(owner).RemoveMemberAsync(tenant.Id, adminId),
            () => _api.CreateClient(admin).InviteMemberAsync(tenant.Id, NewEmail()));

        responses[0].StatusCode.ShouldBe(HttpStatusCode.NoContent);
        responses[1].StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(responses[1])).ShouldBe("tenant.not_found");
        (await InvitationsOfAsync(tenant.Id)).ShouldBe(0);
    }

    // I1, I5: the same key with the same request answers with the invitation the first one created, and sends no second email.
    [Fact]
    public async Task InviteMember_TheSameIdempotencyKeyTwice_CreatesOneInvitationAndSendsOneEmail()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = _api.CreateClient(await _catalog.AddMemberAsync(tenant.Id, role: "Owner"));
        var email = NewEmail();
        var key = Guid.NewGuid().ToString();
        var first = await _api.WaitingForMessagesAsync(() => owner.InviteMemberAsync(tenant.Id, email, idempotencyKey: key));

        var again = await _api.WaitingForMessagesAsync(() => owner.InviteMemberAsync(tenant.Id, email, idempotencyKey: key));

        again.StatusCode.ShouldBe(HttpStatusCode.Created);
        again.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        (await IdOfAsync(again)).ShouldBe(await IdOfAsync(first));
        (await InvitationsOfAsync(tenant.Id)).ShouldBe(1);
        _api.Email.Attempts.Count(attempt => attempt.Email.To == email).ShouldBe(1);
    }

    [Fact]
    public async Task InviteMember_ANewInvitation_EmailsTheTenantsNameAndTheLinkToTheInvitedAddress()
    {
        var tenant = await _catalog.AddTenantAsync(name: "Globex Corp");
        var owner = _api.CreateClient(await _catalog.AddMemberAsync(tenant.Id, role: "Owner"));
        var email = NewEmail();

        var invitation = await IdOfAsync(await _api.WaitingForMessagesAsync(() => owner.InviteMemberAsync(tenant.Id, email)));

        var sent = _api.Email.Sent.Where(message => message.To == email).ShouldHaveSingleItem();
        sent.IdempotencyKey.ShouldBe($"invite/{invitation}");
        sent.Subject.ShouldContain("Globex Corp");
        FakeIdentityProvider.AcceptLinkOf(_api.Email.LinkSentTo(email)).ToString().ShouldStartWith($"{ApiFactory.AcceptUrl}?code=");
        _api.Email.CodeSentFor(invitation).ShouldStartWith($"{tenant.Id}.");
    }

    // The email has the owner invitation's retries: a send that fails is tried again.
    [Fact]
    public async Task InviteMember_TheEmailServiceFailsOnce_SendsTheEmailOnTheNextTry()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = _api.CreateClient(await _catalog.AddMemberAsync(tenant.Id, role: "Owner"));
        var email = NewEmail();
        _api.Email.FailNext(1);

        await _api.TrackMessagesAsync(() => owner.InviteMemberAsync(tenant.Id, email));

        _api.Email.Attempts.Count(attempt => attempt.Email.To == email).ShouldBe(2);
        _api.Email.Sent.Where(message => message.To == email).ShouldHaveSingleItem();
    }

    // O4: an event that arrives twice sends one email, because the email's idempotency key is the invitation's.
    [Fact]
    public async Task SendMemberInvitationEmail_MemberInvitationReadyArrivesTwice_SendsOneEmail()
    {
        var invited = new MemberInvitationReady(
            Guid.CreateVersion7(),
            _time.GetUtcNow(),
            Guid.NewGuid(),
            Guid.CreateVersion7(),
            NewEmail(),
            "Acme Ltd",
            new Uri($"{ApiFactory.AcceptUrl}?code=x"),
            Language.En,
            TimeSpan.FromDays(7));
        using var scope = _api.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var inTheTenant = new DeliveryOptions { TenantId = invited.TenantId.ToString() };

        await _api.WaitingForMessagesAsync(async () => { await bus.PublishAsync(invited, inTheTenant); return 0; });
        await _api.WaitingForMessagesAsync(async () => { await bus.PublishAsync(invited, inTheTenant); return 0; });

        _api.Email.Attempts.Count(attempt => attempt.Email.To == invited.Email).ShouldBe(2);
        _api.Email.Sent.Where(message => message.To == invited.Email).ShouldHaveSingleItem();
    }

    // K3: no record holds an email address; the record names the invitation by its id.
    [Fact]
    public async Task InviteMember_ANewInvitation_LeavesTheEmailOutOfTheAuditLog()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = _api.CreateClient(await _catalog.AddMemberAsync(tenant.Id, role: "Owner"));
        var email = NewEmail();

        var invitation = await IdOfAsync(await _api.WaitingForMessagesAsync(() => owner.InviteMemberAsync(tenant.Id, email)));

        (await _audit.TenantLogAsync(invitation)).ShouldNotBeNull();
        (await _audit.TenantRecordsHoldingAsync(tenant.Id, email)).ShouldBe(0);
        (await _audit.TenantRecordsHoldingAsync(tenant.Id, "@")).ShouldBe(0);
    }

    // Runs the two requests while another transaction holds the tenant row, until both wait for it, then lets them go.
    private async Task<HttpStatusCode[]> WhileTheTenantIsHeldAsync(Guid tenantId, Func<Task<HttpResponseMessage>> one, Func<Task<HttpResponseMessage>> other)
    {
        await using var holder = new NpgsqlConnection(database.SuperuserConnectionString);
        await holder.OpenAsync(Cancellation);
        await using var holding = await holder.BeginTransactionAsync(Cancellation);
        await using var lockTenant = new NpgsqlCommand($"SELECT id FROM control_plane.tenants WHERE id = '{tenantId}' FOR UPDATE", holder, holding);
        await lockTenant.ExecuteNonQueryAsync(Cancellation);

        var requests = new[] { one(), other() };
        await Waiting.UntilAsync(async () => requests.All(request => request.IsCompleted) || await WaitingBehindAsync(holder.ProcessID) == 2);
        await holding.CommitAsync(Cancellation);
        var responses = await Task.WhenAll(requests);

        return [.. responses.Select(response => response.StatusCode)];
    }

    // How many sessions wait behind the holder's session: for its lock, or behind a session that waits for it. A second waiter for a row
    // waits for the first one, which holds the row's place in the queue.
    private Task<long> WaitingBehindAsync(int holder) =>
        database.ScalarAsSuperuserAsync<long>(
            $"""
            WITH RECURSIVE waiting AS (
                SELECT pid FROM pg_stat_activity WHERE {holder} = ANY(pg_blocking_pids(pid))
                UNION
                SELECT activity.pid FROM pg_stat_activity AS activity JOIN waiting ON waiting.pid = ANY(pg_blocking_pids(activity.pid))
            )
            SELECT count(*) FROM waiting
            """);

    private (string Invitee, string Email) InviteeWithAnAccount()
    {
        var invitee = $"user_{Guid.NewGuid():N}";
        var email = NewEmail();
        _api.Identity.AddAccount(invitee, email);
        return (invitee, email);
    }

    private Task<HttpResponseMessage> AcceptAsync(string userId, string code) =>
        _api.CreateClient(userId).PostAsJsonAsync("/v1/me/invitations/accept", new { code, firstName = "Ada", lastName = "Lovelace" }, Cancellation);

    private Task<long> InvitationsOfAsync(Guid tenantId) =>
        database.ScalarAsSuperuserAsync<long>($"SELECT count(*) FROM control_plane.invitations WHERE tenant_id = '{tenantId}'");

    private Task<long> PendingInvitationsOfAsync(Guid tenantId) =>
        database.ScalarAsSuperuserAsync<long>($"SELECT count(*) FROM control_plane.invitations WHERE tenant_id = '{tenantId}' AND status = 'Pending'");

    private static (string? Role, string? ReplacedInvitationId) Details(string? json)
    {
        var details = JsonDocument.Parse(json!).RootElement;
        return (details.GetProperty("role").GetString(), details.GetProperty("replacedInvitationId").GetString());
    }

    private static string NewEmail() => $"{Guid.NewGuid():N}@example.com";

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Cancellation))!.Extensions["code"]?.ToString();
}
