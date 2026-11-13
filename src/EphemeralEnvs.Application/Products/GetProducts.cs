using EphemeralEnvs.Application.Abstractions;
using EphemeralEnvs.Application.Persistence;
using EphemeralEnvs.Contracts;

using Microsoft.EntityFrameworkCore;

namespace EphemeralEnvs.Application.Products;

/// <summary>All products, newest first.</summary>
public sealed record GetProductsQuery;

internal sealed class GetProductsHandler(IReadDb db) : IQueryHandler<GetProductsQuery, ProductDto[]>
{
    public Task<ProductDto[]> HandleAsync(GetProductsQuery query, CancellationToken cancellationToken) =>
        db.Products
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProductDto(p.Id, p.Name, p.Price, p.CreatedAt))
            .ToArrayAsync(cancellationToken);
}
