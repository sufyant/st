using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Delivery;
using Tenancy;
using Testcontainers.Redis;
using Wolverine;

namespace Api.IntegrationTests;

// In-app notifications reach a signed-in user's open connections in real time (0037).
public sealed class NotificationHubTests(Database database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly Catalog _catalog = new(database);

    [Fact]
    public async Task Only_a_signed_in_user_connects()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString);

        var response = await api.CreateClient().PostAsync("/v1/notifications/hub/negotiate?negotiateVersion=1", null, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // A browser cannot send a header when it opens a WebSocket, so on the hub the token may come in the query string.
    [Fact]
    public async Task On_the_hub_the_token_may_come_in_the_query_string()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString);
        var user = await _catalog.AddUserAsync();

        var response = await api.CreateClient().PostAsync(
            $"/v1/notifications/hub/negotiate?negotiateVersion=1&access_token={TestTokens.For(user)}", null, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Anywhere else the token comes only in the header, so it does not end up in URLs that are logged or cached.
    [Fact]
    public async Task Outside_the_hub_a_token_in_the_query_string_is_ignored()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString);
        var tenant = await _catalog.AddTenantAsync();
        var member = await _catalog.AddMemberAsync(tenant.Id);

        var response = await api.CreateClient().GetAsync(
            $"/v1/tenants/{tenant.Slug}/notifications/scheduled?access_token={TestTokens.For(member)}", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_due_notification_reaches_its_users_open_connection()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString);
        var (tenant, user) = await TenantWithMemberAsync();
        await using var connection = await ConnectAsync(api, user);
        var received = Receive(connection);
        var notification = await AddDueNotificationAsync(tenant, user);

        await DispatchAsync(api, tenant, notification);

        var delivered = await received.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        delivered.GetProperty("id").GetGuid().ShouldBe(notification);
        delivered.GetProperty("tenantId").GetGuid().ShouldBe(tenant);
        delivered.GetProperty("title").GetString().ShouldBe("Call Grace");
    }

    // With several pods, the pod that sends a notification is often not the one holding the user's connection; the Redis
    // backplane carries it across (0037, 0038).
    [Fact]
    public async Task With_the_redis_backplane_a_notification_sent_on_one_pod_reaches_a_connection_on_another()
    {
        await using var redis = new RedisBuilder("redis:7.4").Build();
        await redis.StartAsync(Cancellation);
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = redis.GetConnectionString() };
        await using var holding = new ApiFactory(database.ApplicationConnectionString, settings: settings);
        await using var sending = new ApiFactory(database.ApplicationConnectionString, settings: settings);
        var (tenant, user) = await TenantWithMemberAsync();
        await using var connection = await ConnectAsync(holding, user);
        var received = Receive(connection);
        var notification = await AddDueNotificationAsync(tenant, user);

        await DispatchAsync(sending, tenant, notification);

        (await received.WaitAsync(TimeSpan.FromSeconds(10), Cancellation)).GetProperty("id").GetGuid().ShouldBe(notification);
    }

    private async Task<(Guid Tenant, string User)> TenantWithMemberAsync()
    {
        var tenant = await _catalog.AddTenantAsync();
        return (tenant.Id, await _catalog.AddMemberAsync(tenant.Id));
    }

    private static async Task<HubConnection> ConnectAsync(ApiFactory api, string user)
    {
        api.CreateClient();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(api.Server.BaseAddress, "/v1/notifications/hub"), options =>
            {
                options.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(TestTokens.For(user));
            })
            .Build();
        await connection.StartAsync(Cancellation);

        return connection;
    }

    private static Task<JsonElement> Receive(HubConnection connection)
    {
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("notification", notification => received.TrySetResult(notification));
        return received.Task;
    }

    // Written as the owner, who is not bound by row level security, already due, so the test needs no clock of its own.
    private async Task<Guid> AddDueNotificationAsync(Guid tenant, string user)
    {
        var id = Guid.NewGuid();
        await database.ScalarAsync<object>(
            $"""
            INSERT INTO notifications.scheduled_notifications (id, tenant_id, recipient_id, title, body, due_at, created_at, status)
            VALUES ('{id}', '{tenant}', '{user}', 'Call Grace', 'About the launch', now() - interval '1 minute', now() - interval '1 day', 'Scheduled')
            """,
            DatabaseRoles.Owner);

        return id;
    }

    private static async Task DispatchAsync(ApiFactory api, Guid tenant, Guid notification)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeForTenantAsync(tenant.ToString(), new DispatchScheduledNotification(notification), Cancellation);
    }
}
