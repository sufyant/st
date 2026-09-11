using Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Infrastructure.Persistence;

public sealed class AuditInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State is EntityState.Added)
            {
                entry.CurrentValues[nameof(IAuditable.CreatedAt)] = now;
                entry.CurrentValues[nameof(IAuditable.UpdatedAt)] = now;
            }
            else if (entry.State is EntityState.Modified)
            {
                entry.CurrentValues[nameof(IAuditable.UpdatedAt)] = now;
            }
        }

        // Assigning roles/permissions through a skip navigation only changes the join row
        // (e.g. user_roles), leaving the owning User/Role entry Unchanged. Walk those join
        // rows and stamp their IAuditable principal directly, since it would otherwise miss
        // the audit stamp entirely.
        StampPrincipalsOfChangedJoinRows(context, now);
    }

    private static void StampPrincipalsOfChangedJoinRows(DbContext context, DateTimeOffset now)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Deleted) || !entry.Metadata.IsPropertyBag)
            {
                continue;
            }

            foreach (var foreignKey in entry.Metadata.GetForeignKeys())
            {
                if (!typeof(IAuditable).IsAssignableFrom(foreignKey.PrincipalEntityType.ClrType))
                {
                    continue;
                }

                var principalEntry = FindTrackedPrincipal(context, entry, foreignKey);

                if (principalEntry is { State: EntityState.Unchanged })
                {
                    principalEntry.Property(nameof(IAuditable.UpdatedAt)).CurrentValue = now;
                    principalEntry.Property(nameof(IAuditable.UpdatedAt)).IsModified = true;
                }
            }
        }
    }

    private static EntityEntry? FindTrackedPrincipal(DbContext context, EntityEntry joinEntry, IForeignKey foreignKey)
    {
        var keyValues = foreignKey.Properties
            .Select(property => joinEntry.Property(property.Name).CurrentValue)
            .ToArray();

        return context.ChangeTracker.Entries().FirstOrDefault(candidate =>
            candidate.Entity.GetType() == foreignKey.PrincipalEntityType.ClrType &&
            KeysMatch(candidate, foreignKey.PrincipalKey.Properties, keyValues));
    }

    private static bool KeysMatch(EntityEntry candidate, IReadOnlyList<IProperty> keyProperties, object?[] keyValues)
    {
        for (var i = 0; i < keyProperties.Count; i++)
        {
            if (!Equals(candidate.Property(keyProperties[i].Name).CurrentValue, keyValues[i]))
            {
                return false;
            }
        }

        return true;
    }
}
