using System.Security.Claims;
using Api.Application;
using Api.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Api.Tests.Unit.Infrastructure;

public class PermissionBehaviorTests
{
    private sealed record UnprotectedCommand : IRequest<Result>;

    [Fact]
    public async Task Handle_NoRequiresPermissionAttribute_CallsNextWhenHttpContextIsNull()
    {
        // Arrange
        var httpContextAccessor = new HttpContextAccessor { HttpContext = null };
        var behavior = new PermissionBehavior<UnprotectedCommand, Result>(httpContextAccessor);
        var nextCalled = false;
        Task<Result> Next()
        {
            nextCalled = true;
            return Task.FromResult(Result.Success());
        }

        // Act
        var result = await behavior.Handle(new UnprotectedCommand(), Next, CancellationToken.None);

        // Assert
        Assert.True(nextCalled);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_NoRequiresPermissionAttribute_CallsNextForAuthenticatedUserWithNoPermissionClaims()
    {
        // Arrange
        var identity = new ClaimsIdentity(authenticationType: "TestAuth");
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        var behavior = new PermissionBehavior<UnprotectedCommand, Result>(httpContextAccessor);
        var nextCalled = false;
        Task<Result> Next()
        {
            nextCalled = true;
            return Task.FromResult(Result.Success());
        }

        // Act
        var result = await behavior.Handle(new UnprotectedCommand(), Next, CancellationToken.None);

        // Assert
        Assert.True(nextCalled);
        Assert.True(result.IsSuccess);
    }
}
