namespace EphemeralEnvs.Contracts;

/// <summary>
/// Feature flag names, shared by Api and Web (configuration section FeatureManagement). Each app declares the flags
/// it uses in its appsettings.json (ArchitectureTests.FeatureFlagTests checks both directions).
/// </summary>
public static class FeatureFlags
{
    /// <summary>GET /products/stats (Api), stats bar in the widget, stats panel on the Web's product list.</summary>
    public const string ProductStats = "ProductStats";

    /// <summary>"New" badge on products created in the last 24 hours (Web view only, situation C).</summary>
    public const string NewBadge = "NewBadge";

    public static IReadOnlyList<string> All { get; } = [ProductStats, NewBadge];
}
