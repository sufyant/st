namespace Domain.Authorization;

// A tenant whose last owner is removed, disabled or demoted can only be recovered by editing the
// database by hand, so the rule lives here rather than in whichever handler happens to need it.
public sealed class OwnerRoster
{
    private readonly IReadOnlyCollection<Guid> activeOwnerIds;

    private OwnerRoster(IReadOnlyCollection<Guid> activeOwnerIds)
    {
        this.activeOwnerIds = activeOwnerIds;
    }

    public static OwnerRoster Of(IReadOnlyCollection<Guid> activeOwnerIds)
    {
        ArgumentNullException.ThrowIfNull(activeOwnerIds);

        return new OwnerRoster(activeOwnerIds);
    }

    public bool IsLastOwner(Guid userId) =>
        activeOwnerIds.Count == 1 && activeOwnerIds.Single() == userId;
}
