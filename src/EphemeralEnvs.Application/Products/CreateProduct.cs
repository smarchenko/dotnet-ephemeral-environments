using EphemeralEnvs.Application.Abstractions;
using EphemeralEnvs.Data;
using EphemeralEnvs.Domain;

namespace EphemeralEnvs.Application.Products;

/// <summary>Creates a product. Returns its id; read it back through <see cref="GetProductByIdQuery"/>.</summary>
public sealed record CreateProductCommand(string Name, decimal Price);

internal sealed class CreateProductHandler(AppDbContext db, TimeProvider timeProvider)
    : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var product = new Product(command.Name, command.Price, timeProvider.GetUtcNow());
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        return product.Id;
    }
}
