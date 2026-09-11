using Domain.Shared;

namespace Domain.ControlPlane.Administration;

public readonly record struct PlatformAdminId(Guid Value)
{
    public static PlatformAdminId New() => new(Guid.CreateVersion7());

    public static PlatformAdminId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Platform admin ID cannot be empty.", nameof(value))
        : new PlatformAdminId(value);
}

public sealed class PlatformAdmin : Entity<PlatformAdminId>, IAuditable
{
    public ExternalUserId ExternalUserId { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private PlatformAdmin()
    {
    }

    public static PlatformAdmin Create(ExternalUserId externalUserId)
    {
        ArgumentNullException.ThrowIfNull(externalUserId);

        return new PlatformAdmin
        {
            Id = PlatformAdminId.New(),
            ExternalUserId = externalUserId
        };
    }
}
