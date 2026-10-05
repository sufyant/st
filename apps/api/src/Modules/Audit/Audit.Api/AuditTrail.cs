using System.Text.Json;
using Audit.Contracts;
using SharedKernel;
using Wolverine;

namespace Audit.Api;

/// <summary>
/// How the host's pipeline records to the audit log (0040). The record is published on the bus it is given: inside a handler it joins
/// the handler's outbox and tenant transaction; on a request's bus it is stored on its own, in the request's tenant.
/// </summary>
public static class AuditTrail
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A state-changing command that succeeded, with what it kept of itself and, when it returned one, its result.</summary>
    public static ValueTask RecordCommandAsync(IMessageBus bus, TimeProvider time, IAuditedCommand command, object? result) =>
        RecordAsync(
            bus,
            time,
            command.ActorId,
            AuditKind.Command,
            command.GetType().Name,
            JsonSerializer.Serialize(new { command = command.AuditDetails, result }, Json));

    /// <summary>An authorization attempt denied inside the tenant.</summary>
    public static ValueTask RecordDeniedAsync(IMessageBus bus, TimeProvider time, string actorId, string operation) =>
        RecordAsync(bus, time, actorId, AuditKind.Denied, operation, details: null);

    /// <summary>A system admin's entry into the tenant (0031).</summary>
    public static ValueTask RecordSystemAdminEntryAsync(IMessageBus bus, TimeProvider time, string actorId, string operation) =>
        RecordAsync(bus, time, actorId, AuditKind.SystemAdminEntry, operation, details: null);

    private static ValueTask RecordAsync(
        IMessageBus bus,
        TimeProvider time,
        string actorId,
        AuditKind kind,
        string operation,
        string? details)
    {
        var now = time.GetUtcNow();
        return bus.PublishAsync(new RecordAuditEntry(Guid.CreateVersion7(now), now, actorId, kind, operation, details));
    }
}
