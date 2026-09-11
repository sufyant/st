namespace Domain.Authorization;

// A tenant whose last owner is removed, disabled or demoted can only be recovered by editing the
// database by hand, so the rule lives here rather than in whichever handler happens to need it.
public sealed class OwnerRoster
{
    private readonly IReadOnlyCollection<UserId> activeOwnerIds;

    private OwnerRoster(IReadOnlyCollection<UserId> activeOwnerIds)
    {
        this.activeOwnerIds = activeOwnerIds;
    }

    public static OwnerRoster Of(IReadOnlyCollection<UserId> activeOwnerIds)
    {
        ArgumentNullException.ThrowIfNull(activeOwnerIds);

        return new OwnerRoster(activeOwnerIds);
    }

    public bool IsLastOwner(UserId userId) =>
        activeOwnerIds.Count == 1 && activeOwnerIds.Single() == userId;
}
