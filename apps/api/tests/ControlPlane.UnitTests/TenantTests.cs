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
        var tenant = Tenant.Create(Id, slug);

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
        var tenant = Tenant.Create(Id, slug);

        tenant.IsSuccess.ShouldBeFalse();
        tenant.Error.Code.ShouldBe("tenant.slug_invalid");
    }

    [Fact]
    public void A_new_tenant_is_provisioning()
    {
        var tenant = Tenant.Create(Id, "acme").Value;

        tenant.Status.ShouldBe(TenantStatus.Provisioning);
    }

    [Fact]
    public void An_activated_tenant_is_active()
    {
        var tenant = Tenant.Create(Id, "acme").Value;

        tenant.Activate();

        tenant.Status.ShouldBe(TenantStatus.Active);
    }

    // A tenant's onboarding ends active or failed, never both.
    [Fact]
    public void Only_a_provisioning_tenant_is_activated()
    {
        var tenant = Tenant.Create(Id, "acme").Value;
        tenant.Fail();

        var activated = tenant.Activate();

        activated.ShouldBeFalse();
        tenant.Status.ShouldBe(TenantStatus.Failed);
    }

    [Fact]
    public void A_provisioning_tenant_fails()
    {
        var tenant = Tenant.Create(Id, "acme").Value;

        var failed = tenant.Fail();

        failed.ShouldBeTrue();
        tenant.Status.ShouldBe(TenantStatus.Failed);
    }

    // The invitation's delivery comes after activation and is not compensated, so an active tenant stays active.
    [Fact]
    public void An_active_tenant_does_not_fail()
    {
        var tenant = Tenant.Create(Id, "acme").Value;
        tenant.Activate();

        var failed = tenant.Fail();

        failed.ShouldBeFalse();
        tenant.Status.ShouldBe(TenantStatus.Active);
    }
}
