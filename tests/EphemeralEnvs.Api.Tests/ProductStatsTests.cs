using System.Net;
using System.Net.Http.Json;

using EphemeralEnvs.Contracts;

namespace EphemeralEnvs.Api.Tests;

/// <summary>GET /products/stats behind the ProductStats flag (situation B: the server enforces the flag).</summary>
public sealed class ProductStatsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Stats_FlagOff_Returns404()
    {
        using var factory = new ApiFactory(postgres.ConnectionString, Flag("false"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/products/stats", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Stats_FlagOn_MatchesProductList()
    {
        using var factory = new ApiFactory(postgres.ConnectionString, Flag("true"));
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/products", new CreateProductRequest { Name = "Cheap", Price = 1.00m });
        await client.PostAsJsonAsync("/products", new CreateProductRequest { Name = "Dear", Price = 3.00m });

        // The class's database is shared by its tests: compare with the list, not with fixed numbers.
        var list = await client.GetFromJsonAsync<ProductDto[]>("/products");
        var stats = await client.GetFromJsonAsync<ProductStatsDto>("/products/stats");

        Assert.NotNull(list);
        Assert.NotNull(stats);
        Assert.Equal(list.Length, stats.Count);
        Assert.Equal(list.Average(p => p.Price), stats.AveragePrice);
        Assert.Equal(list.Max(p => p.CreatedAt), stats.NewestCreatedAt);
    }

    internal static Dictionary<string, string?> Flag(string value) => new() { ["FeatureManagement:ProductStats"] = value };
}

/// <summary>Own class = own empty database.</summary>
public sealed class ProductStatsEmptyTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Stats_FlagOn_EmptyDatabase_ReturnsZeroAndNulls()
    {
        using var factory = new ApiFactory(postgres.ConnectionString, ProductStatsTests.Flag("true"));
        using var client = factory.CreateClient();

        var stats = await client.GetFromJsonAsync<ProductStatsDto>("/products/stats");

        Assert.NotNull(stats);
        Assert.Equal(0, stats.Count);
        Assert.Null(stats.AveragePrice);
        Assert.Null(stats.NewestCreatedAt);
    }
}
