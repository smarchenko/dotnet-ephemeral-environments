using EphemeralEnvs.Application.Abstractions;
using EphemeralEnvs.Application.Persistence;
using EphemeralEnvs.Contracts;

using Microsoft.EntityFrameworkCore;

namespace EphemeralEnvs.Application.Products;

/// <summary>One product, or <c>null</c> if it does not exist.</summary>
public sealed record GetProductByIdQuery(Guid Id);

internal sealed class GetProductByIdHandler(IReadDb db) : IQueryHandler<GetProductByIdQuery, ProductDto?>
{
    public Task<ProductDto?> HandleAsync(GetProductByIdQuery query, CancellationToken cancellationToken) =>
        db.Products
            .Where(p => p.Id == query.Id)
            .Select(p => new ProductDto(p.Id, p.Name, p.Price, p.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);
}
