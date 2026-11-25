using EphemeralEnvs.Contracts;

namespace EphemeralEnvs.Web.Models;

/// <summary>
/// The products page. <paramref name="ShowStats"/> = the Web's ProductStats flag; then either <paramref name="Stats"/>
/// or <paramref name="StatsMessage"/> (API flag off, or the call failed) is set.
/// </summary>
public sealed record HomeIndexModel(
    IReadOnlyList<ProductDto> Products,
    bool ShowStats,
    ProductStatsDto? Stats,
    string? StatsMessage)
{
    /// <summary>NewBadge marks products created within this window.</summary>
    public static readonly TimeSpan NewWindow = TimeSpan.FromHours(24);

    public static bool IsNew(ProductDto product, DateTimeOffset now) => product.CreatedAt >= now - NewWindow;
}
