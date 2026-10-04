using System.Net;
using System.Net.Http.Json;
using Wolverine;
using Wolverine.Tracking;

namespace Api.IntegrationTests;

public sealed class TenantTransactionTests(Database database) : IAsyncLifetime
{
    private readonly Catalog _catalog = new(database);
    private PipelineHost _host = null!;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await PipelineHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_command_from_a_tenant_request_runs_under_that_tenant()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = _host.CreateClient(await _catalog.AddMemberAsync(tenant.Id));

        await member.PostAsJsonAsync($"/v1/tenants/{tenant.Slug}/probes", new { value = "mine" }, Cancellation);

        var probes = await member.GetFromJsonAsync<string[]>($"/v1/tenants/{tenant.Slug}/probes", Cancellation);
        probes.ShouldBe(["mine"]);
    }

    [Fact]
    public async Task A_tenant_request_does_not_see_the_data_of_another_tenant()
    {
        var tenant = await _catalog.AddTenantAsync();
        var other = await _catalog.AddTenantAsync();
        var otherMember = _host.CreateClient(await _catalog.AddMemberAsync(other.Id));
        await otherMember.PostAsJsonAsync($"/v1/tenants/{other.Slug}/probes", new { value = "theirs" }, Cancellation);

        var probes = await _host.CreateClient(await _catalog.AddMemberAsync(tenant.Id))
            .GetFromJsonAsync<string[]>($"/v1/tenants/{tenant.Slug}/probes", Cancellation);

        probes.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_command_that_fails_leaves_nothing_written()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = _host.CreateClient(await _catalog.AddMemberAsync(tenant.Id));

        var response = await member.PostAsJsonAsync($"/v1/tenants/{tenant.Slug}/failing-probes", new { value = "lost" }, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await member.GetFromJsonAsync<string[]>($"/v1/tenants/{tenant.Slug}/probes", Cancellation)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_command_that_returns_a_failure_leaves_nothing_written()
    {
        var tenant = await _catalog.AddTenantAsync();
        var member = _host.CreateClient(await _catalog.AddMemberAsync(tenant.Id));

        var response = await member.PostAsJsonAsync($"/v1/tenants/{tenant.Slug}/rejected-probes", new { value = "rejected" }, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await member.GetFromJsonAsync<string[]>($"/v1/tenants/{tenant.Slug}/probes", Cancellation)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_command_that_returns_a_failure_with_a_value_type_leaves_nothing_written()
    {
        var tenant = Guid.NewGuid();

        var result = await Bus().InvokeForTenantAsync<SharedKernel.Result<Guid>>(
            tenant.ToString(), new WriteProbeThenRejectWithValue("rejected"), Cancellation);

        result.IsSuccess.ShouldBeFalse();
        (await ReadProbesAsync(tenant)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_message_carries_its_tenant_into_the_transaction()
    {
        var tenant = Guid.NewGuid();

        var setting = await Bus().InvokeForTenantAsync<string?>(tenant.ToString(), new ReadTenantSetting(), Cancellation);

        setting.ShouldBe(tenant.ToString());
    }

    [Fact]
    public async Task A_message_without_a_tenant_sees_no_tenant_data()
    {
        await Bus().InvokeForTenantAsync<SharedKernel.Result>(Guid.NewGuid().ToString(), new WriteProbe("tenant data"), Cancellation);

        var probes = await Bus().InvokeAsync<SharedKernel.Result<string[]>>(new ReadProbes(), Cancellation);

        probes.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_message_with_a_malformed_tenant_is_not_handled()
    {
        var invoke = () => Bus().InvokeForTenantAsync<string?>("not-a-tenant-id", new ReadTenantSetting(), Cancellation);

        await invoke.ShouldThrowAsync<FormatException>();
    }

    [Fact]
    public async Task A_published_message_is_handled_under_its_tenant()
    {
        var tenant = Guid.NewGuid();

        await _host.Host.ExecuteAndWaitAsync(
            context => context.PublishAsync(new WriteProbe("published"), new DeliveryOptions { TenantId = tenant.ToString() }).AsTask());

        (await ReadProbesAsync(tenant)).ShouldBe(["published"]);
    }

    [Fact]
    public async Task A_cascaded_message_keeps_the_tenant_of_the_message_that_caused_it()
    {
        var tenant = Guid.NewGuid();

        await _host.Host.ExecuteAndWaitAsync(context => context.InvokeForTenantAsync(tenant.ToString(), new WriteProbeLater("cascaded")));

        (await ReadProbesAsync(tenant)).ShouldBe(["cascaded"]);
    }

    private IMessageBus Bus() => _host.Host.MessageBus();

    private async Task<string[]> ReadProbesAsync(Guid tenant)
    {
        var probes = await Bus().InvokeForTenantAsync<SharedKernel.Result<string[]>>(tenant.ToString(), new ReadProbes(), Cancellation);
        return probes.Value;
    }
}
