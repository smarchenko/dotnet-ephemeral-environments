namespace EphemeralEnvs.DbTool.Seeding;

/// <summary>
/// A named, versioned piece of demo data, applied once per database (like a migration). Lives in <c>Seeding/Sets</c>
/// and is found by <see cref="SeedSetCatalog"/>; no registration needed.
/// Rules: never edit a set that may already be applied, add a new one (<c>0002-…</c>) instead; ids are fixed constants.
/// </summary>
internal interface ISeedSet
{
    /// <summary>Journal key and order: <c>NNNN-description</c>, sets run in ordinal name order.</summary>
    public string Name { get; }

    /// <summary>
    /// Builds the set's entities through the domain (e.g. <c>Product.CreateSeeded</c>). Every object must be an entity
    /// of <c>AppDbContext</c> with a fixed key. Called on every run, so it must return the same data each time.
    /// </summary>
    public IEnumerable<object> Build();
}

internal enum SeedMode
{
    /// <summary>Apply only sets that are not in the journal yet.</summary>
    Normal,

    /// <summary>Ignore the journal: insert seed entities that are missing (e.g. deleted by QA); never update existing rows.</summary>
    Restore,
}

/// <summary>What happened to one set.</summary>
internal sealed record SeedSetResult(string Name, bool SkippedAsApplied, int Inserted, int AlreadyPresent);
