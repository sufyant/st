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
    public void CreateTenant_UrlSafeSlug_IsCreatedWithThatSlug(string slug)
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
    public void CreateTenant_SlugNotUrlSafe_IsRejected(string slug)
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
    public void CreateTenant_New_IsProvisioning()
    {
        var tenant = Tenant.Create(Id, "Acme Ltd", "acme").Value;

        tenant.Status.ShouldBe(TenantStatus.Provisioning);
    }

    [Fact]
    public void ActivateTenant_WhenProvisioning_IsActive()
    {
        var tenant = Tenant.Create(Id, "Acme Ltd", "acme").Value;

        tenant.Activate();

        tenant.Status.ShouldBe(TenantStatus.Active);
    }

    // A tenant's onboarding ends active or cancelled, never both.
    [Fact]
    public void ActivateTenant_Cancelled_StaysCancelled()
    {
        var tenant = Tenant.Create(Id, "Acme Ltd", "acme").Value;
        tenant.Cancel("identity_provider_failed");

        var activated = tenant.Activate();

        activated.ShouldBeFalse();
        tenant.Status.ShouldBe(TenantStatus.Cancelled);
    }

    [Fact]
    public void CancelTenant_Provisioning_IsCancelledWithItsReason()
    {
        var tenant = Tenant.Create(Id, "Acme Ltd", "acme").Value;

        var cancelled = tenant.Cancel("identity_provider_failed");

        cancelled.ShouldBeTrue();
        tenant.Status.ShouldBe(TenantStatus.Cancelled);
        tenant.CancellationReason.ShouldBe("identity_provider_failed");
    }

    // A compensation may arrive twice; the first reason stays.
    [Fact]
    public void CancelTenant_AlreadyCancelled_KeepsTheFirstReason()
    {
        var tenant = Tenant.Create(Id, "Acme Ltd", "acme").Value;
        tenant.Cancel("identity_provider_failed");

        var cancelled = tenant.Cancel("registration_timed_out");

        cancelled.ShouldBeFalse();
        tenant.CancellationReason.ShouldBe("identity_provider_failed");
    }

    // Activation is the pivot: an active tenant is not cancelled.
    [Fact]
    public void CancelTenant_Active_StaysActive()
    {
        var tenant = Tenant.Create(Id, "Acme Ltd", "acme").Value;
        tenant.Activate();

        var cancelled = tenant.Cancel("activation_failed");

        cancelled.ShouldBeFalse();
        tenant.Status.ShouldBe(TenantStatus.Active);
        tenant.CancellationReason.ShouldBeNull();
    }
}
