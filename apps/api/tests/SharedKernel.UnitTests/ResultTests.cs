namespace SharedKernel.UnitTests;

public class ResultTests
{
    private static readonly Error TenantNotFound = Error.NotFound("tenant.not_found", "The tenant was not found.");

    [Fact]
    public void A_successful_result_carries_its_value()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void A_failed_result_carries_its_error()
    {
        Result<int> result = TenantNotFound;

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(TenantNotFound);
    }

    [Fact]
    public void Reading_the_value_of_a_failed_result_is_a_programming_error()
    {
        Result<int> result = TenantNotFound;

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Reading_the_error_of_a_successful_result_is_a_programming_error()
    {
        var result = Result.Success();

        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void A_result_without_a_value_can_fail_with_an_error()
    {
        Result result = TenantNotFound;

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(TenantNotFound);
    }
}
