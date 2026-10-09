using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Application.Tenants;

/// <summary>
/// Tells a person that a tenant's onboarding needs attention (S9): an error log entry with a fixed event name and the tenant's id,
/// and one counter. It names no exception: what failed is in the dead letter queue.
/// </summary>
/// <remarks>Public only because Wolverine's generated code passes it to a public handler.</remarks>
public sealed partial class OnboardingAlarm(ILogger<OnboardingAlarm> logger, IMeterFactory meters)
{
    /// <summary>The module's meter; the host exports every meter named <c>Modules.*</c>.</summary>
    public const string MeterName = "Modules.ControlPlane";

    private readonly Counter<long> _needsAttention = meters.Create(MeterName).CreateCounter<long>(
        "tenant_onboarding.needs_attention",
        description: "Tenant onboardings that went to the needs attention state.");

    public void Raise(Guid tenantId)
    {
        LogNeedsAttention(logger, tenantId);
        _needsAttention.Add(1);
    }

    [LoggerMessage(EventId = 1, EventName = "TenantOnboardingNeedsAttention", Level = LogLevel.Error, Message = "The onboarding of tenant {TenantId} needs attention")]
    private static partial void LogNeedsAttention(ILogger logger, Guid tenantId);
}
