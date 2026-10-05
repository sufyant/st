using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Api.IntegrationTests;

// A member schedules notifications for themselves in their tenant (0027); a viewer only reads (0030).
public sealed class NotificationEndpointTests(Database database) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly Catalog _catalog = new(database);
    private ApiFactory _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFactory(database.ApplicationConnectionString);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task A_member_schedules_a_notification_and_finds_it_among_their_own()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id);
        var client = _api.CreateClient(member);
        var dueAt = DateTimeOffset.UtcNow.AddDays(3);

        var scheduled = await client.PostAsJsonAsync(
            $"/v1/tenants/{tenant.Slug}/notifications/scheduled", new { title = "Call Grace", body = "About the launch", dueAt }, Cancellation);
        var listed = await client.GetFromJsonAsync<JsonElement>($"/v1/tenants/{tenant.Slug}/notifications/scheduled", Cancellation);

        scheduled.StatusCode.ShouldBe(HttpStatusCode.OK);
        var notification = await scheduled.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        notification.GetProperty("status").GetString().ShouldBe("Scheduled");
        listed.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe([notification.GetProperty("id").GetGuid()]);
    }

    [Fact]
    public async Task A_member_cancels_their_notification()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id);
        var client = _api.CreateClient(member);
        var scheduled = await (await client.PostAsJsonAsync(
                $"/v1/tenants/{tenant.Slug}/notifications/scheduled",
                new { title = "Call Grace", body = "", dueAt = DateTimeOffset.UtcNow.AddDays(3) },
                Cancellation))
            .Content.ReadFromJsonAsync<JsonElement>(Cancellation);

        var cancelled = await client.DeleteAsync($"/v1/tenants/{tenant.Slug}/notifications/scheduled/{scheduled.GetProperty("id").GetGuid()}", Cancellation);
        var listed = await client.GetFromJsonAsync<JsonElement>($"/v1/tenants/{tenant.Slug}/notifications/scheduled", Cancellation);

        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        listed.GetProperty("items").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("Cancelled");
    }

    [Fact]
    public async Task A_notification_cannot_be_scheduled_in_the_past()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id);

        var response = await _api.CreateClient(member).PostAsJsonAsync(
            $"/v1/tenants/{tenant.Slug}/notifications/scheduled",
            new { title = "Call Grace", body = "", dueAt = DateTimeOffset.UtcNow.AddMinutes(-1) },
            Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_viewer_cannot_schedule_a_notification()
    {
        var tenant = await _catalog.AddTenantAsync();
        var viewer = await _catalog.AddMemberAsync(tenant.Id, role: "Viewer");

        var response = await _api.CreateClient(viewer).PostAsJsonAsync(
            $"/v1/tenants/{tenant.Slug}/notifications/scheduled",
            new { title = "Call Grace", body = "", dueAt = DateTimeOffset.UtcNow.AddDays(3) },
            Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
