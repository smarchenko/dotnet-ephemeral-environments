using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using EphemeralEnvs.Application.Abstractions;
using EphemeralEnvs.Application.Products;
using EphemeralEnvs.Contracts;
using EphemeralEnvs.Domain;

using Microsoft.Extensions.DependencyInjection;

namespace EphemeralEnvs.Api.Tests;

/// <summary>
/// Validation lives in two places: DTO attributes (friendly 400 at the API boundary) and domain invariants
/// (the write model protects itself, whoever sends the command). Limit sync is checked in ArchitectureTests/ContractTests.
/// </summary>
public sealed class DomainValidationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task CreateProductHandler_InvalidCommand_ThrowsAndSavesNothing()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var create = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateProductCommand, Guid>>();
        var getAll = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetProductsQuery, ProductDto[]>>();
        var before = await getAll.HandleAsync(new GetProductsQuery(), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => create.HandleAsync(new CreateProductCommand("Pen", 0m), CancellationToken.None));

        Assert.Equal(nameof(Product.Price), ex.Field);
        var after = await getAll.HandleAsync(new GetProductsQuery(), CancellationToken.None);
        Assert.Equal(before.Length, after.Length);
    }

    [Fact]
    public async Task DomainValidationException_Returns400ProblemDetails_WithFieldError()
    {
        // Simulates a rule that only the domain knows about: the DTO is valid, the domain rejects it.
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            configureServices: services => services.AddScoped<ICommandHandler<CreateProductCommand, Guid>, RejectingHandler>());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/products", new CreateProductRequest("Pen", 1m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors").GetProperty("Name");
        Assert.Equal("Rejected by the domain.", errors[0].GetString());
    }

    private sealed class RejectingHandler : ICommandHandler<CreateProductCommand, Guid>
    {
        public Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken) =>
            throw new DomainValidationException(nameof(Product.Name), "Rejected by the domain.");
    }
}
