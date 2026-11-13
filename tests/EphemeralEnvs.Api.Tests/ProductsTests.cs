using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using EphemeralEnvs.Contracts;

namespace EphemeralEnvs.Api.Tests;

public sealed class ProductsTests : IClassFixture<PostgresFixture>, IDisposable
{
    private const string ProblemJson = "application/problem+json";

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ProductsTests(PostgresFixture postgres)
    {
        _factory = new ApiFactory(postgres.ConnectionString);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Post_ValidProduct_Returns201_AndIsRetrievableById_AndInList()
    {
        var response = await _client.PostAsJsonAsync("/products", new CreateProductRequest("Pen", 1.50m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var created = await response.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(created);

        var byId = await _client.GetFromJsonAsync<ProductDto>(response.Headers.Location);
        Assert.NotNull(byId);
        Assert.Equal(created.Id, byId.Id);
        Assert.Equal("Pen", byId.Name);
        Assert.Equal(1.50m, byId.Price);

        var all = await _client.GetFromJsonAsync<ProductDto[]>("/products");
        Assert.NotNull(all);
        Assert.Contains(all, p => p.Id == created.Id);
    }

    [Fact]
    public async Task Post_EmptyName_Returns400ProblemDetails()
    {
        var response = await _client.PostAsJsonAsync("/products", new CreateProductRequest("", 1m));

        await AssertValidationProblemAsync(response, "Name");
    }

    [Fact]
    public async Task Post_WhitespaceName_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/products", new CreateProductRequest("   ", 1m));

        await AssertValidationProblemAsync(response, "Name");
    }

    [Fact]
    public async Task Post_NameOf201Chars_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/products", new CreateProductRequest(new string('a', 201), 1m));

        await AssertValidationProblemAsync(response, "Name");
    }

    [Fact]
    public async Task Post_ZeroPrice_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/products", new CreateProductRequest("Pen", 0m));

        await AssertValidationProblemAsync(response, "Price");
    }

    [Fact]
    public async Task Post_PriceTooLarge_Returns400()
    {
        // Does not fit numeric(18,2): must be rejected by validation, not fail in the database (500).
        var response = await _client.PostAsJsonAsync("/products", new CreateProductRequest("Pen", 100_000_000_000_000_000m));

        await AssertValidationProblemAsync(response, "Price");
    }

    [Fact]
    public async Task Post_PriceWithThreeDecimals_Returns400()
    {
        // numeric(18,2) would silently store 1.01 instead of 1.005.
        var response = await _client.PostAsJsonAsync("/products", new CreateProductRequest("Pen", 1.005m));

        await AssertValidationProblemAsync(response, "Price");
    }

    [Fact]
    public async Task Get_UnknownId_Returns404ProblemDetails()
    {
        var response = await _client.GetAsync(new Uri($"/products/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_NonGuidId_Returns404()
    {
        var response = await _client.GetAsync(new Uri("/products/not-a-guid", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(
            errors.EnumerateObject().Any(e => e.Name.Equals(field, StringComparison.OrdinalIgnoreCase)),
            $"Expected a validation error for '{field}', got: {errors}");
    }
}
