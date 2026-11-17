using EphemeralEnvs.DbTool.Seeding;

namespace EphemeralEnvs.DbTool.Tests;

/// <summary>A seed set defined by a test. <paramref name="build"/> runs on every <see cref="Build"/> call, like a real set.</summary>
internal sealed class TestSeedSet(string name, Func<IEnumerable<object>> build) : ISeedSet
{
    public string Name => name;

    public IEnumerable<object> Build() => build();
}
