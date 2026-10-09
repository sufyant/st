using ControlPlane.Domain.Tenants;

namespace ControlPlane.UnitTests;

// Section 8: a tenant creation request carries an idempotency key. The same key with the same request means the same tenant; the
// same key with another request is a mistake.
public class TenantCreationRequestTests
{
    private static readonly Guid SystemAdmin = new("0199a8f0-0000-7000-8000-000000000101");
    private static readonly Guid TenantId = new("0199a8f0-0000-7000-8000-000000000201");
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RepeatRequest_TheSameNameSlugAndOwner_IsTheSameRequest()
    {
        var first = TenantCreationRequest.Record(SystemAdmin, "key-1", "Acme Ltd", "acme", "ali@acme.com", TenantId, Now);

        var same = first.IsFor("Acme Ltd", "acme", "ali@acme.com");

        same.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Acme Inc", "acme", "ali@acme.com")]
    [InlineData("Acme Ltd", "acme-ltd", "ali@acme.com")]
    [InlineData("Acme Ltd", "acme", "hank@acme.com")]
    [InlineData("Acme Ltdacme", "", "ali@acme.com")]
    public void RepeatRequest_AnotherField_IsAnotherRequest(string name, string slug, string ownerEmail)
    {
        var first = TenantCreationRequest.Record(SystemAdmin, "key-1", "Acme Ltd", "acme", "ali@acme.com", TenantId, Now);

        var same = first.IsFor(name, slug, ownerEmail);

        same.ShouldBeFalse();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("0199a8f0-0000-7000-8000-000000000301")]
    [InlineData("!~")]
    public void CheckKey_OneTo255VisibleAsciiCharacters_IsAKey(string key)
    {
        TenantCreationRequest.IsKey(key).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("tab\there")]
    [InlineData("ключ")]
    public void CheckKey_EmptyOrNotVisibleAscii_IsNotAKey(string? key)
    {
        TenantCreationRequest.IsKey(key).ShouldBeFalse();
    }

    [Fact]
    public void CheckKey_LongerThan255Characters_IsNotAKey()
    {
        TenantCreationRequest.IsKey(new string('k', 256)).ShouldBeFalse();
    }
}
