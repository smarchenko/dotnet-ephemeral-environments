using EphemeralEnvs.Contracts;

namespace EphemeralEnvs.Api;

/// <summary>
/// Flags the browser may see via GET /features. Anything the browser gets is public, so a flag goes here
/// only when its state is not a secret. The server still enforces every flag on its own endpoints.
/// </summary>
internal static class ClientFeatureFlags
{
    public static IReadOnlyList<string> Names { get; } = [FeatureFlags.ProductStats];
}
