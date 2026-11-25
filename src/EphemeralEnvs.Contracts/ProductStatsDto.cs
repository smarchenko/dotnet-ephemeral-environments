namespace EphemeralEnvs.Contracts;

/// <summary>Product statistics (GET /products/stats, behind the ProductStats flag). Empty table: 0 and nulls.</summary>
public sealed record ProductStatsDto(int Count, decimal? AveragePrice, DateTimeOffset? NewestCreatedAt);
