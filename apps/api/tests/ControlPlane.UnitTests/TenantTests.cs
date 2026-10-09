using ControlPlane.Domain.Tenants;

namespace ControlPlane.UnitTests;

public class TenantTests
{
    private static readonly Guid Id = new("0199a8f0-0000-7000-8000-000000000001");

    [Theory]
    [InlineData("acme")]
    [InlineData("acme-corp")]
    [InlineData("a1b")]
    [InlineData("tenant-2024-eu")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijk")]
    public void A_tenant_is_created_with_a_url_safe_slug(string slug)
    {
        var tenant = Tenant.Create(Id, "Acme Ltd", slug);

        tenant.IsSuccess.ShouldBeTrue();
        tenant.Value.Slug.ShouldBe(slug);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijkl")]
    [InlineData("Acme")]
    [InlineData("acme corp")]
    [InlineData("acme_corp")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("acme--corp")]
    [InlineData("acmé")]
    public void A_slug_that_is_not_url_safe_is_rejected(string slug)
    {
        var tenant = Tenant.Create(Id, "Acme Ltd", slug);

        tenant.IsSuccess.ShouldBeFalse();
        tenant.Error.Code.ShouldBe("tenant.slug_invalid");
    }

    [Fact]
    public void CreateTenant_NameWithSpacesAround_IsKeptWithoutThem()
    {
        var tenant = Tenant.Create(Id, "  Acme Ltd ", "acme");

        tenant.Value.Name.ShouldBe("Acme Ltd");
    }

    [Fact]
    public void CreateTenant_NameOfOneHundredCharacters_IsAccepted()
    {
        var name = new string('n', 100);

        var tenant = Tenant.Create(Id, name, "acme");

        tenant.Value.Name.ShouldBe(name);
    }

    public static TheoryData<string> InvalidNames => ["", "   ", new string('n', 101)];

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public void CreateTenant_InvalidName_IsRejected(string name)
    {
        var tenant = Tenant.Create(Id, name, "acme");

        tenant.IsSuccess.ShouldBeFalse();
        tenant.Error.Code.ShouldBe("tenant.name_invalid");
    }

    [Fact]
    public void A_new_tenant_is_provisioning()
    {
        var tenant = Tenant.Create(Id, "Acme Ltd", "acme").Value;

        tenant.Status.ShouldBe(TenantStatus.Provisioning);
    }

    [Fact]
    public void An_activated_tenant_is_active()
    {
        var tenant = Tenant.Create(Id, "Acme Ltd", "acme").Value;

        tenant.Activate();

        tenant.Status.ShouldBe(TenantStatus.Active);
    }

    // A tenant's onboarding ends active or failed, never both.
    [Fact]
    public void Only_a_provisioning_tenant_is_activated()
    {
        var tenant = Tenant.Create(Id, "Acme Ltd", "acme").Value;
        tenant.Fail();

        var activated = tenant.Activate();

        activated.ShouldBeFalse();
        tenant.Status.ShouldBe(TenantStatus.Failed);
    }

    [Fact]
    public void A_provisioning_tenant_fails()
    {
        var tenant = Tenant.Create(Id, "Acme Ltd", "acme").Value;

        var failed = tenant.Fail();

        failed.ShouldBeTrue();
        tenant.Status.ShouldBe(TenantStatus.Failed);
    }

    // The invitation's delivery comes after activation and is not compensated, so an active tenant stays active.
    [Fact]
    public void An_active_tenant_does_not_fail()
    {
        var tenant = Tenant.Create(Id, "Acme Ltd", "acme").Value;
        tenant.Activate();

        var failed = tenant.Fail();

        failed.ShouldBeFalse();
        tenant.Status.ShouldBe(TenantStatus.Active);
    }
}
