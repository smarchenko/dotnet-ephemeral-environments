using System.Reflection;

using EphemeralEnvs.Data.History;

namespace EphemeralEnvs.DbTool.Seeding;

/// <summary>Finds the seed sets of an assembly (every non-abstract <see cref="ISeedSet"/>) and orders them by name.</summary>
internal static class SeedSetCatalog
{
    public static IReadOnlyList<ISeedSet> Discover(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var sets = assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false } && typeof(ISeedSet).IsAssignableFrom(type))
            .Select(type => (ISeedSet)Activator.CreateInstance(type, nonPublic: true)!);

        return Order(sets);
    }

    /// <summary>Validates names (non-blank, fit the journal, unique) and sorts them ordinally.</summary>
    public static IReadOnlyList<ISeedSet> Order(IEnumerable<ISeedSet> sets)
    {
        var ordered = sets.OrderBy(set => set.Name, StringComparer.Ordinal).ToList();

        foreach (var set in ordered)
        {
            if (string.IsNullOrWhiteSpace(set.Name) || set.Name.Length > DbToolHistoryEntry.NameMaxLength)
            {
                throw new InvalidOperationException(
                    $"Seed set {set.GetType().Name} has an invalid name '{set.Name}' (required, at most {DbToolHistoryEntry.NameMaxLength} characters).");
            }
        }

        var duplicate = ordered.GroupBy(set => set.Name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Seed set name '{duplicate.Key}' is used more than once.");
        }

        return ordered;
    }
}
