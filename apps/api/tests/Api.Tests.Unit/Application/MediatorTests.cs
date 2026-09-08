using Api.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Api.Tests.Unit.Application;

public class MediatorTests
{
    private sealed record Ping(string Message) : IRequest<string>;

    private sealed class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken) =>
            Task.FromResult($"Pong: {request.Message}");
    }

    private sealed class UppercaseBehavior : IPipelineBehavior<Ping, string>
    {
        public async Task<string> Handle(
            Ping request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            var response = await next();
            return response.ToUpperInvariant();
        }
    }

    [Fact]
    public async Task Send_NoBehaviors_InvokesHandlerDirectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IRequestHandler<Ping, string>, PingHandler>();
        var provider = services.BuildServiceProvider();
        var mediator = new Mediator(provider);

        // Act
        var response = await mediator.Send(new Ping("hi"));

        // Assert
        Assert.Equal("Pong: hi", response);
    }

    [Fact]
    public async Task Send_WithBehavior_BehaviorWrapsHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IRequestHandler<Ping, string>, PingHandler>();
        services.AddScoped<IPipelineBehavior<Ping, string>, UppercaseBehavior>();
        var provider = services.BuildServiceProvider();
        var mediator = new Mediator(provider);

        // Act
        var response = await mediator.Send(new Ping("hi"));

        // Assert
        Assert.Equal("PONG: HI", response);
    }
}
