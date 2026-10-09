namespace SharedKernel.UnitTests;

public class ResultTests
{
    private static readonly Error TenantNotFound = Error.NotFound("tenant.not_found", "The tenant was not found.");

    [Fact]
    public void CreateResult_FromAValue_CarriesTheValue()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void CreateResult_FromAnError_CarriesTheError()
    {
        Result<int> result = TenantNotFound;

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(TenantNotFound);
    }

    [Fact]
    public void ReadValue_OfAFailedResult_Throws()
    {
        Result<int> result = TenantNotFound;

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void ReadError_OfASuccessfulResult_Throws()
    {
        var result = Result.Success();

        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void CreateResultWithoutValue_FromAnError_CarriesTheError()
    {
        Result result = TenantNotFound;

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(TenantNotFound);
    }

    [Fact]
    public void MapResult_WhenSuccessful_MapsTheValue()
    {
        Result<int> result = 42;

        var mapped = result.Map(value => $"#{value}");

        mapped.Value.ShouldBe("#42");
    }

    [Fact]
    public void MapResult_WhenFailed_KeepsTheError()
    {
        Result<int> result = TenantNotFound;

        var mapped = result.Map(value => $"#{value}");

        mapped.Error.ShouldBe(TenantNotFound);
    }
}
