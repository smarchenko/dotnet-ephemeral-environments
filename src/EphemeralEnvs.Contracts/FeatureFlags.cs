namespace EphemeralEnvs.Contracts;

/// <summary>
/// Feature flag names, shared by Api and Web (configuration section FeatureManagement). Each app declares the flags
/// it uses in its appsettings.json (ArchitectureTests.FeatureFlagTests checks both directions).
/// </summary>
public static class FeatureFlags
{
    /// <summary>GET /products/stats (Api), stats bar in the widget, stats panel on the Web's product list.</summary>
    public const string ProductStats = "ProductStats";

    public static IReadOnlyList<string> All { get; } = [ProductStats];
}
