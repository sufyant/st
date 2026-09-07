using Api.Domain;
using Xunit;

namespace Api.Tests.Unit.Domain;

public class MoneyTests
{
    [Fact]
    public void Create_ValidInput_NormalizesCurrencyToUppercase()
    {
        // Act
        var money = Money.Create(10.50m, "usd");

        // Assert
        Assert.Equal(10.50m, money.Amount);
        Assert.Equal("USD", money.Currency);
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    public void Create_InvalidCurrency_ThrowsArgumentException(string currency)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Money.Create(1m, currency));
    }

    [Fact]
    public void Add_SameCurrency_ReturnsSum()
    {
        // Arrange
        var first = Money.Create(10m, "USD");
        var second = Money.Create(5m, "USD");

        // Act
        var result = first.Add(second);

        // Assert
        Assert.Equal(Money.Create(15m, "USD"), result);
    }

    [Fact]
    public void Add_DifferentCurrency_ThrowsInvalidOperationException()
    {
        // Arrange
        var usd = Money.Create(10m, "USD");
        var eur = Money.Create(5m, "EUR");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => usd.Add(eur));
    }

    [Fact]
    public void Zero_ReturnsZeroAmountInGivenCurrency()
    {
        // Act
        var zero = Money.Zero("EUR");

        // Assert
        Assert.Equal(0m, zero.Amount);
        Assert.Equal("EUR", zero.Currency);
    }
}
