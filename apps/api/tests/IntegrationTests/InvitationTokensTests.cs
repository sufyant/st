using Infrastructure.Access;
using Xunit;

namespace IntegrationTests;

public sealed class InvitationTokensTests
{
    [Fact]
    public void Create_ProducesDistinctUrlSafeTokens()
    {
        // Arrange & Act
        var first = InvitationTokens.Create();
        var second = InvitationTokens.Create();

        // Assert
        Assert.NotEqual(first, second);
        Assert.DoesNotContain('+', first);
        Assert.DoesNotContain('/', first);
        Assert.DoesNotContain('=', first);
        Assert.True(first.Length >= 32);
    }

    [Fact]
    public void Hash_IsStableAndHidesTheToken()
    {
        // Arrange
        var token = InvitationTokens.Create();

        // Act
        var hash = InvitationTokens.Hash(token);

        // Assert
        Assert.Equal(hash, InvitationTokens.Hash(token));
        Assert.DoesNotContain(token, hash, StringComparison.Ordinal);
        Assert.Equal(64, hash.Length);
    }
}
