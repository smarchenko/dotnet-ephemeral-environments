namespace EphemeralEnvs.Data.History;

/// <summary>
/// One row of <c>__db_tool_history</c>: a one-off data operation of EphemeralEnvs.DbTool that has been applied
/// to this database (a seed set now, a backfill in stage 4c). Persistence bookkeeping like
/// <c>__EFMigrationsHistory</c>, not a domain entity. Internal: only DbTool (and its tests) may touch the journal
/// (InternalsVisibleTo in the csproj), so other code cannot mark a seed/backfill as applied.
/// </summary>
internal sealed class DbToolHistoryEntry
{
    /// <summary>Longest allowed <see cref="Name"/>.</summary>
    public const int NameMaxLength = 200;

    /// <summary>Longest allowed <see cref="Kind"/>.</summary>
    public const int KindMaxLength = 50;

    public DbToolHistoryEntry(string name, string kind, DateTimeOffset appliedAt)
    {
        Name = name;
        Kind = kind;
        AppliedAt = appliedAt;
    }

    /// <summary>Operation name, e.g. the seed set <c>products-v1</c>. Primary key.</summary>
    public string Name { get; private set; }

    /// <summary>See <see cref="DbToolHistoryKinds"/>.</summary>
    public string Kind { get; private set; }

    public DateTimeOffset AppliedAt { get; private set; }
}

/// <summary>Values of <see cref="DbToolHistoryEntry.Kind"/>.</summary>
internal static class DbToolHistoryKinds
{
    public const string Seed = "seed";
}
