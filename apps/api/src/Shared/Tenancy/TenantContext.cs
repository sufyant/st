namespace Tenancy;

/// <summary>The tenant the current request or message runs under; set once by the host pipeline.</summary>
public sealed class TenantContext
{
    public Guid? TenantId { get; private set; }

    public void Set(Guid tenantId)
    {
        if (TenantId is { } current && current != tenantId)
        {
            throw new InvalidOperationException("The tenant of a request or message cannot change once it is set.");
        }

        TenantId = tenantId;
    }
}
