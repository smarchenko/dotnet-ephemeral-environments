using EphemeralEnvs.Data;
using EphemeralEnvs.Data.History;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace EphemeralEnvs.DbTool.Seeding;

/// <summary>
/// Applies seed sets once per database: applied sets are recorded in <c>__db_tool_history</c>. Knows no entity type:
/// any entity of <see cref="AppDbContext"/> works, its key comes from the EF model. Existing rows (by key) are never
/// updated, so data changed by hand survives every run. EF orders the inserts by foreign keys.
/// </summary>
internal sealed partial class SeedRunner(AppDbContext db, TimeProvider time, ILogger<SeedRunner> logger)
{
    public async Task<IReadOnlyList<SeedSetResult>> RunAsync(IReadOnlyList<ISeedSet> sets, SeedMode mode, CancellationToken ct)
    {
        if (sets.Count == 0)
        {
            LogNoSets(logger);
            return [];
        }

        var results = new List<SeedSetResult>(sets.Count);
        foreach (var set in sets)
        {
            results.Add(await ApplyAsync(set, mode, ct));
        }

        return results;
    }

    private async Task<SeedSetResult> ApplyAsync(ISeedSet set, SeedMode mode, CancellationToken ct)
    {
        // Build and validate the whole set before touching the database.
        var entities = Resolve(set);

        var applied = await db.DbToolHistory.AnyAsync(e => e.Name == set.Name, ct);
        if (applied && mode == SeedMode.Normal)
        {
            LogSkipped(logger, set.Name);
            return new SeedSetResult(set.Name, SkippedAsApplied: true, Inserted: 0, AlreadyPresent: 0);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var inserted = 0;
        foreach (var (entity, entityType, key) in entities)
        {
            if (await db.FindAsync(entityType.ClrType, key, ct) is null)
            {
                db.Add(entity);
                inserted++;
            }
        }

        if (!applied)
        {
            db.DbToolHistory.Add(new DbToolHistoryEntry(set.Name, DbToolHistoryKinds.Seed, time.GetUtcNow()));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();

        var alreadyPresent = entities.Count - inserted;
        LogApplied(logger, mode == SeedMode.Restore ? "restored" : "applied", set.Name, inserted, alreadyPresent);
        return new SeedSetResult(set.Name, SkippedAsApplied: false, Inserted: inserted, AlreadyPresent: alreadyPresent);
    }

    /// <summary>Builds the set (domain validation happens here) and finds each object's entity type and key.</summary>
    private List<(object Entity, IEntityType EntityType, object?[] Key)> Resolve(ISeedSet set)
    {
        var resolved = new List<(object, IEntityType, object?[])>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in set.Build())
        {
            var entityType = db.Model.FindEntityType(entity.GetType());
            if (entityType is null || entityType.IsOwned())
            {
                throw new InvalidDataException(
                    $"Seed set '{set.Name}' contains a {entity.GetType().Name}, which is not an entity of {nameof(AppDbContext)}.");
            }

            var key = entityType.FindPrimaryKey()!.Properties.Select(p => p.GetGetter().GetClrValue(entity)).ToArray();
            var keyText = string.Join(", ", key);
            if (!seen.Add($"{entityType.Name}|{keyText}"))
            {
                throw new InvalidDataException(
                    $"Seed set '{set.Name}' contains {entity.GetType().Name} with key {keyText} more than once.");
            }

            resolved.Add((entity, entityType, key));
        }

        return resolved;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "No seed sets found")]
    private static partial void LogNoSets(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped {SeedSet} (already applied)")]
    private static partial void LogSkipped(ILogger logger, string seedSet);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Action} {SeedSet}: inserted {Inserted}, already present {AlreadyPresent}")]
    private static partial void LogApplied(ILogger logger, string action, string seedSet, int inserted, int alreadyPresent);
}
