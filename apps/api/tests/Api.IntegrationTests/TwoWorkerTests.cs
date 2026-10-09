using System.Net;
using ControlPlane.Contracts;

namespace Api.IntegrationTests;

// Requirements 3 and 5 of the messaging tool (section 3): with two workers on one database, only one of them takes a message at a
// time, and a message that a dying worker was handling is taken over by the other. Wolverine's guarantee is at least once: the
// other worker runs the handler again, so the handler's second run must have no effect (O4).
public sealed class TwoWorkerTests(Database database)
{
    [Fact]
    public async Task HandleQueuedMessages_TwoWorkers_EachIsHandledOnceAndBothWorkersTakePart()
    {
        await using var workers = await TwoWorkers.StartAsync(database);
        Guid[] ids = [.. Enumerable.Range(0, 200).Select(_ => Guid.NewGuid())];

        await workers.SendFromWebAsync(ids.Select(id => new RecordHandling(id)));
        await workers.WaitForHandledAsync<RecordHandling>(200);

        var handlings = await workers.Handlings.ReadAsync();
        handlings.Select(handling => handling.MessageId).ShouldBe(ids, ignoreOrder: true);
        workers.Handlings.Runs.Count.ShouldBe(200);
        handlings.Select(handling => handling.Worker).Distinct().ShouldBe([TwoWorkers.First, TwoWorkers.Second], ignoreOrder: true);
        (await workers.DeadLettersAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task HandleQueuedMessage_WorkerDiesAfterTheEffect_TheOtherWorkerRunsItAgainWithoutASecondEffect()
    {
        await using var workers = await TwoWorkers.StartAsync(database);
        var id = Guid.NewGuid();

        await workers.SendFromWebAsync([new RecordHandlingThenHold(id)]);
        var dead = await workers.Hold.HeldInAsync();
        await workers.CrashAsync(dead);
        await workers.WaitForHandledAsync<RecordHandlingThenHold>(1);

        (await workers.Handlings.ReadAsync()).ShouldBe([(id, dead)]);
        workers.Handlings.Runs.ShouldBe([dead, workers.Survivor(dead)]);
        (await workers.DeadLettersAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task OnboardTenant_WorkerDiesWhileTheSagaWaitsForTheEmail_TheOtherWorkerCompletesIt()
    {
        await using var workers = await TwoWorkers.StartAsync(database);
        var admin = workers.Web.CreateClient(await workers.AddSystemAdminAsync(), secondFactor: true);

        var created = await admin.CreateTenantAsync(new { name = "Acme Ltd", slug = "acme", ownerEmail = "owner@acme.test" });
        var tenantId = await TwoWorkers.IdOfAsync(created);
        var dead = await workers.Hold.HeldInAsync();
        await workers.WaitForOnboardingStateAsync(tenantId, "SendingInvitation");
        await workers.CrashAsync(dead);
        await workers.WaitForOnboardingStateAsync(tenantId, "Completed");

        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await workers.OnboardingStateAsync(tenantId)).ShouldBe("Completed");
        workers.Email.Sent.ShouldHaveSingleItem().To.ShouldBe("owner@acme.test");
        workers.Runs.In(workers.Survivor(dead)).ShouldContain(typeof(OwnerInvitationReady));
        workers.Runs.In(dead).ShouldNotContain(typeof(OwnerInvitationReady));
    }
}
