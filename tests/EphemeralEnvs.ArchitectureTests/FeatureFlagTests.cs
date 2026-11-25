using System.Reflection;
using System.Text.Json;

using EphemeralEnvs.Contracts;

namespace EphemeralEnvs.ArchitectureTests;

/// <summary>
/// Flag names live in <see cref="FeatureFlags"/>; each app declares the flags it uses in its appsettings.json.
/// These rules catch a constant missing from <see cref="FeatureFlags.All"/>, a retired flag left in a config file
/// and a constant no app declares.
/// </summary>
public sealed class FeatureFlagTests
{
    private static readonly string[] AppSettingsFiles =
    [
        Path.Combine("src", "EphemeralEnvs.Api", "appsettings.json"),
        Path.Combine("src", "EphemeralEnvs.Web", "appsettings.json"),
    ];

    [Fact]
    public void All_ListsEveryFlagConstant_ExactlyOnce()
    {
        var constants = typeof(FeatureFlags)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(constants, FeatureFlags.All.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AppSettings_DeclareOnlyKnownFlags()
    {
        var unknown = AppSettingsFiles
            .SelectMany(file => DeclaredFlags(file).Select(flag => (file, flag)))
            .Where(x => !FeatureFlags.All.Contains(x.flag))
            .Select(x => $"{x.file}: FeatureManagement:{x.flag} is not in FeatureFlags.All (retired flag left in config?)")
            .ToList();

        Assert.True(unknown.Count == 0, string.Join(Environment.NewLine, unknown));
    }

    [Fact]
    public void EveryFlag_IsDeclaredBySomeApp()
    {
        var declared = AppSettingsFiles.SelectMany(DeclaredFlags).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var undeclared = FeatureFlags.All.Where(flag => !declared.Contains(flag)).ToList();

        Assert.True(undeclared.Count == 0,
            $"Not declared in any appsettings.json FeatureManagement section: {string.Join(", ", undeclared)}");
    }

    private static IEnumerable<string> DeclaredFlags(string relativePath)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot.Value, relativePath)));
        return json.RootElement.TryGetProperty("FeatureManagement", out var section)
            ? section.EnumerateObject().Select(p => p.Name).ToList()
            : [];
    }

    private static readonly Lazy<string> RepoRoot = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EphemeralEnvs.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("EphemeralEnvs.slnx not found above " + AppContext.BaseDirectory);
    });
}
