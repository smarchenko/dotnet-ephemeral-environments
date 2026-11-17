using EphemeralEnvs.DbTool.Seeding;
using EphemeralEnvs.DbTool.Seeding.Sets;
using EphemeralEnvs.Domain;

namespace EphemeralEnvs.DbTool.Tests;

public sealed class SeedSetCatalogTests
{
    [Fact]
    public void Discover_FindsShippedSets()
    {
        var sets = SeedSetCatalog.Discover(typeof(DbToolCli).Assembly);

        Assert.Equal(["0001-products"], sets.Select(s => s.Name));
    }

    [Fact]
    public void Order_SortsByNameOrdinal()
    {
        var sets = SeedSetCatalog.Order([Empty("0002-b"), Empty("0010-c"), Empty("0001-a")]);

        Assert.Equal(["0001-a", "0002-b", "0010-c"], sets.Select(s => s.Name));
    }

    [Fact]
    public void Order_DuplicateName_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SeedSetCatalog.Order([Empty("0001-a"), Empty("0001-a")]));

        Assert.Contains("0001-a", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Order_BlankName_Throws(string name)
    {
        Assert.Throws<InvalidOperationException>(() => SeedSetCatalog.Order([Empty(name)]));
    }

    [Fact]
    public void Order_NameLongerThanJournalColumn_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => SeedSetCatalog.Order([Empty(new string('a', 201))]));
    }

    [Fact]
    public void Seed0001Products_BuildsFiveProductsWithFixedIds()
    {
        var products = new Seed0001Products().Build().Cast<Product>().ToList();

        Assert.Equal(
            Enumerable.Range(1, 5).Select(i => Guid.Parse($"0196a000-0000-7000-8000-00000000000{i}")),
            products.Select(p => p.Id));
        Assert.Equal(new Seed0001Products().Build().Cast<Product>().Select(p => p.Id), products.Select(p => p.Id));
    }

    private static TestSeedSet Empty(string name) => new(name, () => []);
}
