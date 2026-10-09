using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tenancy;

namespace Api.IntegrationTests;

// A system admin creates a tenant through the system door, and the onboarding saga runs through the outbox. TenantOnboardingProcessTests
// follows the saga itself.
public sealed class TenantOnboardingEndpointTests(Database database) : IAsyncLifetime
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
    public async Task A_new_tenant_answers_as_provisioning_and_becomes_active_with_its_first_owner_invited()
    {
        var admin = await AdminAsync();
        var slug = Slug();
        var owner = $"{Guid.NewGuid():N}@example.com";

        var created = await _api.WaitingForMessagesAsync(() => admin.CreateTenantAsync(new { name = "Acme Ltd", slug, ownerEmail = owner }));

        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await created.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("status").GetString().ShouldBe("Provisioning");
        (await StatusOfAsync(slug)).ShouldBe("Active");
        _api.Email.Sent.ShouldContain(sent => sent.To == owner);
    }

    [Fact]
    public async Task A_slug_another_tenant_has_is_a_conflict()
    {
        var admin = await AdminAsync();
        var existing = await _catalog.AddTenantAsync();

        var created = await admin.CreateTenantAsync(new { name = "Acme Ltd", slug = existing.Slug, ownerEmail = "owner@example.com" });

        created.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await created.Content.ReadFromJsonAsync<ProblemDetails>(Cancellation))!.Extensions["code"]?.ToString().ShouldBe("tenant.slug_taken");
    }

    [Theory]
    [InlineData("Not A Slug", "owner@example.com")]
    [InlineData("valid-slug", "not an email")]
    public async Task A_tenant_needs_a_url_safe_slug_and_an_owner_email(string slug, string ownerEmail)
    {
        var admin = await AdminAsync();

        var created = await admin.CreateTenantAsync(new { name = "Acme Ltd", slug, ownerEmail });

        created.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateTenant_WithNameSlugAndOwnerEmail_ReturnsTheTenant()
    {
        var admin = await AdminAsync();
        var slug = Slug();

        var created = await _api.WaitingForMessagesAsync(() =>
            admin.CreateTenantAsync(new { name = "Acme Ltd", slug, ownerEmail = "ali@acme.com" }));

        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tenant = await created.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        tenant.EnumerateObject().Select(field => field.Name).ShouldBe(["id", "name", "slug", "status"], ignoreOrder: true);
        tenant.GetProperty("id").GetGuid().ShouldBe(await database.ScalarAsync<Guid>($"SELECT id FROM catalog.tenants WHERE slug = '{slug}'"));
        tenant.GetProperty("name").GetString().ShouldBe("Acme Ltd");
        tenant.GetProperty("slug").GetString().ShouldBe(slug);
        tenant.GetProperty("status").GetString().ShouldBe("Provisioning");
        (await database.ScalarAsync<string>($"SELECT name FROM catalog.tenants WHERE slug = '{slug}'")).ShouldBe("Acme Ltd");
    }

    // T5: the system door is /v1/system; the old path is gone.
    [Fact]
    public async Task CreateTenant_OnTheOldPath_IsNotFound()
    {
        var admin = await AdminAsync();

        var created = await admin.PostAsJsonAsync("/v1/admin/tenants", new { name = "Acme Ltd", slug = Slug(), ownerEmail = "ali@acme.com" }, Cancellation);

        created.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    public static TheoryData<string> InvalidNames => ["", "   ", new string('n', 101)];

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public async Task CreateTenant_InvalidName_IsRejected(string name)
    {
        var admin = await AdminAsync();

        var created = await admin.CreateTenantAsync(new { name, slug = Slug(), ownerEmail = "ali@acme.com" });

        created.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await created.Content.ReadFromJsonAsync<ProblemDetails>(Cancellation))!.Extensions["code"]?.ToString().ShouldBe("tenant.name_invalid");
    }

    [Fact]
    public async Task CreateTenant_WithoutAName_IsRejected()
    {
        var admin = await AdminAsync();

        var created = await admin.CreateTenantAsync(new { slug = Slug(), ownerEmail = "ali@acme.com" });

        created.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // OWASP API8: a field the request leaves out, or sends as null, is a validation problem, never a server error.
    [Theory]
    [InlineData("""{"name":"Acme Ltd","ownerEmail":"ali@acme.com"}""", "Slug")]
    [InlineData("""{"name":"Acme Ltd","slug":null,"ownerEmail":"ali@acme.com"}""", "Slug")]
    [InlineData("""{"name":"Acme Ltd","slug":"acme-ltd"}""", "OwnerEmail")]
    [InlineData("""{"name":"Acme Ltd","slug":"acme-ltd","ownerEmail":null}""", "OwnerEmail")]
    [InlineData("""{"slug":"acme-ltd","ownerEmail":"ali@acme.com"}""", "Name")]
    [InlineData("""{"name":null,"slug":"acme-ltd","ownerEmail":"ali@acme.com"}""", "Name")]
    public async Task CreateTenant_WithoutARequiredField_IsAValidationProblem(string body, string field)
    {
        var admin = await AdminAsync();

        var created = await admin.CreateTenantAsync(new StringContent(body, Encoding.UTF8, "application/json"));

        created.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await created.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Cancellation))!.Errors.Keys.ShouldBe([field]);
    }

    // A body the endpoint cannot read at all is a bad request in every environment, never a server error.
    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{")]
    public async Task CreateTenant_WithoutAReadableBody_IsABadRequest(string body)
    {
        var admin = await AdminAsync();

        var created = await admin.CreateTenantAsync(new StringContent(body, Encoding.UTF8, "application/json"));

        created.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        created.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task CreateTenant_NameOfOneHundredCharacters_IsAccepted()
    {
        var admin = await AdminAsync();
        var name = new string('n', 100);

        var created = await _api.WaitingForMessagesAsync(() =>
            admin.CreateTenantAsync(new { name, slug = Slug(), ownerEmail = "ali@acme.com" }));

        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await created.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("name").GetString().ShouldBe(name);
    }

    [Fact]
    public async Task A_member_cannot_create_a_tenant()
    {
        var tenant = await _catalog.AddTenantAsync();
        var owner = await _catalog.AddMemberAsync(tenant.Id, role: "Owner");

        var created = await _api.CreateClient(owner, secondFactor: true)
            .CreateTenantAsync(new { name = "Acme Ltd", slug = Slug(), ownerEmail = "owner@example.com" });

        created.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // Section 8: tenant creation carries an idempotency key, so a request sent twice does not create a second tenant.
    [Fact]
    public async Task CreateTenant_WithoutAnIdempotencyKey_IsABadRequest()
    {
        var admin = await AdminAsync();
        var slug = Slug();

        var created = await admin.PostAsJsonAsync(TenantRequests.Path, new { name = "Acme Ltd", slug, ownerEmail = "ali@acme.com" }, Cancellation);

        created.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await created.Content.ReadFromJsonAsync<ProblemDetails>(Cancellation))!.Extensions["code"]?.ToString().ShouldBe("idempotency_key_invalid");
        (await CountAsync($"SELECT count(*) FROM catalog.tenants WHERE slug = '{slug}'")).ShouldBe(0);
    }

    public static TheoryData<string> InvalidIdempotencyKeys => ["", "has space", "tab\there", "ключ", new string('k', 256)];

    [Theory]
    [MemberData(nameof(InvalidIdempotencyKeys))]
    public async Task CreateTenant_WithAnInvalidIdempotencyKey_IsABadRequest(string key)
    {
        var admin = await AdminAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, TenantRequests.Path)
        {
            Content = JsonContent.Create(new { name = "Acme Ltd", slug = Slug(), ownerEmail = "ali@acme.com" }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key).ShouldBeTrue();

        var created = await admin.SendAsync(request, Cancellation);

        created.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await created.Content.ReadFromJsonAsync<ProblemDetails>(Cancellation))!.Extensions["code"]?.ToString().ShouldBe("idempotency_key_invalid");
    }

    [Fact]
    public async Task CreateTenant_AKeyOfTwoHundredFiftyFiveVisibleCharacters_IsAccepted()
    {
        var admin = await AdminAsync();

        var created = await admin.CreateTenantAsync(new { name = "Acme Ltd", slug = Slug(), ownerEmail = "ali@acme.com" }, new string('~', 255));

        created.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateTenant_TheSameKeyAndBodyAgain_ReturnsTheFirstTenantAndCreatesNothingNew()
    {
        var admin = await AdminAsync();
        var slug = Slug();
        var body = new { name = "Acme Ltd", slug, ownerEmail = "ali@acme.com" };
        var key = Guid.NewGuid().ToString();
        var first = await _api.WaitingForMessagesAsync(() => admin.CreateTenantAsync(body, key));

        var again = await _api.WaitingForMessagesAsync(() => admin.CreateTenantAsync(body, key));

        var tenantId = await IdOfAsync(first);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await IdOfAsync(again)).ShouldBe(tenantId);
        (await CountAsync($"SELECT count(*) FROM catalog.tenants WHERE slug = '{slug}'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM catalog.invitations WHERE tenant_id = '{tenantId}'")).ShouldBe(1);
    }

    [Fact]
    public async Task CreateTenant_TheSameKeyWithAnotherBody_IsUnprocessable()
    {
        var admin = await AdminAsync();
        var key = Guid.NewGuid().ToString();
        await admin.CreateTenantAsync(new { name = "Acme Ltd", slug = Slug(), ownerEmail = "ali@acme.com" }, key);
        var otherSlug = Slug();

        var reused = await admin.CreateTenantAsync(new { name = "Acme Ltd", slug = otherSlug, ownerEmail = "ali@acme.com" }, key);

        reused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await reused.Content.ReadFromJsonAsync<ProblemDetails>(Cancellation))!.Extensions["code"]?.ToString().ShouldBe("idempotency_key_reused");
        (await CountAsync($"SELECT count(*) FROM catalog.tenants WHERE slug = '{otherSlug}'")).ShouldBe(0);
    }

    [Fact]
    public async Task CreateTenant_TwoRequestsWithTheSameKeyAtOnce_CreateOneTenant()
    {
        var admin = await AdminAsync();
        var slug = Slug();
        var body = new { name = "Acme Ltd", slug, ownerEmail = "ali@acme.com" };
        var key = Guid.NewGuid().ToString();

        var created = await Task.WhenAll(admin.CreateTenantAsync(body, key), admin.CreateTenantAsync(body, key));

        created.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.OK, HttpStatusCode.OK]);
        (await IdOfAsync(created[0])).ShouldBe(await IdOfAsync(created[1]));
        (await CountAsync($"SELECT count(*) FROM catalog.tenants WHERE slug = '{slug}'")).ShouldBe(1);
    }

    // A key belongs to the system admin who sent it.
    [Fact]
    public async Task CreateTenant_TwoSystemAdminsWithTheSameKey_EachCreateTheirOwnTenant()
    {
        var first = await AdminAsync();
        var second = await AdminAsync();
        var key = Guid.NewGuid().ToString();
        var firstSlug = Slug();
        var secondSlug = Slug();

        var byFirst = await first.CreateTenantAsync(new { name = "Acme Ltd", slug = firstSlug, ownerEmail = "ali@acme.com" }, key);
        var bySecond = await second.CreateTenantAsync(new { name = "Globex", slug = secondSlug, ownerEmail = "hank@globex.com" }, key);

        byFirst.StatusCode.ShouldBe(HttpStatusCode.OK);
        bySecond.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await IdOfAsync(bySecond)).ShouldNotBe(await IdOfAsync(byFirst));
    }

    private async Task<HttpClient> AdminAsync() => _api.CreateClient(await _catalog.AddSystemAdminAsync(), secondFactor: true);

    private static async Task<Guid> IdOfAsync(HttpResponseMessage created) =>
        (await created.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();

    private Task<long> CountAsync(string sql) => database.ScalarAsSuperuserAsync<long>(sql);

    private static string Slug() => $"tenant-{Guid.NewGuid():N}"[..20];

    private Task<string?> StatusOfAsync(string slug) => database.ScalarAsync<string>($"SELECT status FROM catalog.tenants WHERE slug = '{slug}'");
}
