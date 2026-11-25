using EphemeralEnvs.Application.Abstractions;
using EphemeralEnvs.Application.Persistence;
using EphemeralEnvs.Contracts;

using Microsoft.EntityFrameworkCore;

namespace EphemeralEnvs.Application.Products;

/// <summary>Count, average price and newest creation time of all products.</summary>
public sealed record GetProductStatsQuery;

internal sealed class GetProductStatsHandler(IReadDb db) : IQueryHandler<GetProductStatsQuery, ProductStatsDto>
{
    public async Task<ProductStatsDto> HandleAsync(GetProductStatsQuery query, CancellationToken cancellationToken)
    {
        // One query: grouping by a constant gives a single row (none for an empty table).
        var stats = await db.Products
            .GroupBy(_ => 1)
            .Select(g => new ProductStatsDto(g.Count(), g.Average(p => (decimal?)p.Price), g.Max(p => (DateTimeOffset?)p.CreatedAt)))
            .SingleOrDefaultAsync(cancellationToken);

        return stats ?? new ProductStatsDto(0, null, null);
    }
}
