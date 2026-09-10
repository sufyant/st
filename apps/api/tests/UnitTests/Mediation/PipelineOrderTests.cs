using Application.Abstractions;
using Application.Mediation;
using Application.Results;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace UnitTests.Mediation;

public sealed record TracedCommand : ICommand<Unit>;

public sealed class TracedCommandHandler(List<string> trace) : IRequestHandler<TracedCommand, Unit>
{
    public Task<Result<Unit>> HandleAsync(TracedCommand request, CancellationToken cancellationToken)
    {
        trace.Add("handler");

        return Task.FromResult(Result.Success());
    }
}

public sealed class OuterBehavior(List<string> trace) : IPipelineBehavior<TracedCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        TracedCommand request,
        RequestHandlerDelegate<Unit> next,
        CancellationToken cancellationToken)
    {
        trace.Add("outer:before");
        var result = await next();
        trace.Add("outer:after");

        return result;
    }
}

public sealed class InnerBehavior(List<string> trace) : IPipelineBehavior<TracedCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        TracedCommand request,
        RequestHandlerDelegate<Unit> next,
        CancellationToken cancellationToken)
    {
        trace.Add("inner:before");
        var result = await next();
        trace.Add("inner:after");

        return result;
    }
}

public sealed class PipelineOrderTests
{
    [Fact]
    public async Task Behaviors_RunOutsideInInRegistrationOrder()
    {
        // Arrange
        var trace = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddScoped<IMediator, Mediator>();
        services.AddScoped<IRequestHandler<TracedCommand, Unit>, TracedCommandHandler>();
        services.AddScoped<IPipelineBehavior<TracedCommand, Unit>, OuterBehavior>();
        services.AddScoped<IPipelineBehavior<TracedCommand, Unit>, InnerBehavior>();
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        // Act
        await mediator.SendAsync(new TracedCommand(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["outer:before", "inner:before", "handler", "inner:after", "outer:after"],
            trace);
    }
}
