using EphemeralEnvs.Application.Abstractions;
using EphemeralEnvs.Application.Products;
using EphemeralEnvs.Contracts;

using Microsoft.AspNetCore.Mvc;

namespace EphemeralEnvs.Api.Controllers;

/// <summary>HTTP only: maps requests to commands/queries and results to status codes.</summary>
[ApiController]
[Route("products")]
public sealed class ProductsController : ControllerBase
{
    [HttpGet]
    public Task<ProductDto[]> GetAll(
        [FromServices] IQueryHandler<GetProductsQuery, ProductDto[]> getProducts,
        CancellationToken cancellationToken) =>
        getProducts.HandleAsync(new GetProductsQuery(), cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDto>> GetById(
        Guid id,
        [FromServices] IQueryHandler<GetProductByIdQuery, ProductDto?> getProductById,
        CancellationToken cancellationToken)
    {
        var product = await getProductById.HandleAsync(new GetProductByIdQuery(id), cancellationToken);
        return product is null ? NotFound() : product;
    }

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(
        CreateProductRequest request,
        [FromServices] ICommandHandler<CreateProductCommand, Guid> createProduct,
        [FromServices] IQueryHandler<GetProductByIdQuery, ProductDto?> getProductById,
        CancellationToken cancellationToken)
    {
        var id = await createProduct.HandleAsync(new CreateProductCommand(request.Name, request.Price), cancellationToken);

        // The command returns only the id; the response body comes from the read side.
        var product = await getProductById.HandleAsync(new GetProductByIdQuery(id), cancellationToken)
            ?? throw new InvalidOperationException($"Product {id} was created but cannot be read back.");

        return CreatedAtAction(nameof(GetById), new { id }, product);
    }
}
