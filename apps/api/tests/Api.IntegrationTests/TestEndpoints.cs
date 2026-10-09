using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Time.Testing;
using SharedKernel;
using Wolverine;

namespace Api.IntegrationTests;

internal static class TestEndpoints
{
    // A permission no built-in role holds: no tenant capability has one yet.
    public const string GuardedPermission = "test.guarded";

    // Stand-ins for host behaviour that is not about who the caller is, so they let anyone in.
    public static void Map(RouteGroupBuilder v1)
    {
        var open = v1.MapGroup("").AllowAnonymous();
        MapOpen(open);

        // Every other endpoint of the version group needs a signed-in user.
        v1.MapGet("/whoami", (ClaimsPrincipal user) => Results.Ok(user.FindFirstValue(ClaimTypes.NameIdentifier)));
    }

    private static void MapOpen(RouteGroupBuilder v1)
    {
        v1.MapPost("/greetings", (Greet command, IMessageBus bus, CancellationToken cancellationToken) =>
            bus.InvokeAsync<Result<Greeting>>(command, cancellationToken));

        v1.MapPost("/acknowledgements", (Acknowledge command, IMessageBus bus, CancellationToken cancellationToken) =>
            bus.InvokeAsync<Result>(command, cancellationToken));

        v1.MapPost("/failures/{type}", (ErrorType type, IMessageBus bus, CancellationToken cancellationToken) =>
            bus.InvokeAsync<Result>(new Fail(type), cancellationToken));

        v1.MapPost("/explosions", (IMessageBus bus, CancellationToken cancellationToken) =>
            bus.InvokeAsync<Result>(new Explode(), cancellationToken));

        v1.MapPost("/slow-work", (DoSlowWork command, IMessageBus bus, CancellationToken cancellationToken) =>
            bus.InvokeAsync<Result>(command, cancellationToken));

        v1.MapGet("/ping", () => Results.Ok());
    }

    public static void MapTenant(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/ping", () => Results.Ok());

        tenant.MapPost("/probes", (WriteProbe command, IMessageBus bus, CancellationToken cancellationToken) =>
            bus.InvokeAsync<Result>(command, cancellationToken));

        tenant.MapPost("/failing-probes", (WriteProbeThenFail command, IMessageBus bus, CancellationToken cancellationToken) =>
            bus.InvokeAsync(command, cancellationToken));

        tenant.MapPost("/rejected-probes", (WriteProbeThenReject command, IMessageBus bus, CancellationToken cancellationToken) =>
            bus.InvokeAsync<Result>(command, cancellationToken));

        tenant.MapGet("/probes", (IMessageBus bus, CancellationToken cancellationToken) =>
            bus.InvokeAsync<Result<string[]>>(new ReadProbes(), cancellationToken));

        tenant.MapPost("/guarded", () => Results.Ok()).RequireAuthorization(GuardedPermission);
    }

    public static void MapSystem(RouteGroupBuilder system) => system.MapGet("/ping", () => Results.Ok());
}

// Wolverine only discovers public handlers, messages and validators.
public sealed record Greet(string Name);

public sealed record Greeting(string Text);

public sealed class GreetValidator : AbstractValidator<Greet>
{
    public GreetValidator() => RuleFor(command => command.Name).NotEmpty();
}

public static class GreetHandler
{
    public static Result<Greeting> Handle(Greet command) => new Greeting($"Hello, {command.Name}");
}

public sealed record Acknowledge;

public static class AcknowledgeHandler
{
    public static Result Handle(Acknowledge command) => Result.Success();
}

public sealed record Fail(ErrorType Type);

public static class FailHandler
{
    public static Result Handle(Fail command) => new Error("test.failure", "The test asked for a failure.", command.Type);
}

public sealed record Explode;

public static class ExplodeHandler
{
    public static Result Handle(Explode command) =>
        throw new InvalidOperationException("Host=internal-db;Password=hunter2");
}

public sealed record DoSlowWork(TimeSpan Duration);

public static class DoSlowWorkHandler
{
    public static Result Handle(DoSlowWork command, FakeTimeProvider time)
    {
        time.Advance(command.Duration);
        return Result.Success();
    }
}
