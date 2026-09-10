using Application.Abstractions;
using Application.Mediation;
using Application.Results;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace UnitTests.Mediation;

public sealed record EchoQuery(string Text) : IQuery<string>;

public sealed class EchoQueryHandler : IRequestHandler<EchoQuery, string>
{
    public Task<Result<string>> HandleAsync(EchoQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(Result<string>.Success(request.Text));
}

public sealed record UnregisteredQuery : IQuery<string>;

public sealed class MediatorTests
{
    [Fact]
    public async Task SendAsync_RoutesToTheRegisteredHandler()
    {
        // Arrange
        var mediator = BuildMediator(services =>
            services.AddScoped<IRequestHandler<EchoQuery, string>, EchoQueryHandler>());

        // Act
        var result = await mediator.SendAsync(
            new EchoQuery("hello"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public async Task SendAsync_ResolvesTheSameRequestTypeTwice()
    {
        // Arrange
        var mediator = BuildMediator(services =>
            services.AddScoped<IRequestHandler<EchoQuery, string>, EchoQueryHandler>());

        // Act
        await mediator.SendAsync(new EchoQuery("first"), TestContext.Current.CancellationToken);
        var result = await mediator.SendAsync(
            new EchoQuery("second"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("second", result.Value);
    }

    [Fact]
    public async Task SendAsync_NamesTheRequestWhenNoHandlerIsRegistered()
    {
        // Arrange
        var mediator = BuildMediator(_ => { });

        // Act
        var act = async () => await mediator.SendAsync(
            new UnregisteredQuery(),
            TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains(nameof(UnregisteredQuery), exception.Message);
    }

    private static IMediator BuildMediator(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, Mediator>();
        configure(services);

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }
}
