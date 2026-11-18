using EphemeralEnvs.Data.History;
using EphemeralEnvs.DbTool.Seeding;
using EphemeralEnvs.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EphemeralEnvs.DbTool.Tests;

[Collection(PostgresGroup.Name)]
public sealed class SeedRunnerTests(PostgresDatabaseFixture postgres)
{
    private static readonly Guid Id1 = Guid.Parse("0196a000-0000-7000-8000-000000000001");
    private static readonly Guid Id2 = Guid.Parse("0196a000-0000-7000-8000-000000000002");
    private static readonly Guid Id3 = Guid.Parse("0196a000-0000-7000-8000-000000000003");
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly TestSeedSet V1 = new("0001-products",
        () => [Product(Id1, "Notebook A5", 4.50m), Product(Id2, "Ballpoint pen", 1.20m)]);

    private static readonly TestSeedSet V2 = new("0002-products", () => [Product(Id3, "Desk lamp", 34.99m)]);

    [Fact]
    public async Task Seed_EmptyDb_InsertsAllWithFixedIdsAndJournal()
    {
        var cs = await postgres.CreateDatabaseAsync();

        var results = await RunAsync(cs, [V1], SeedMode.Normal);

        Assert.Equal([new SeedSetResult("0001-products", false, 2, 0)], results);
        await using var db = PostgresDatabaseFixture.CreateContext(cs);
        Assert.Equal(new[] { Id1, Id2 }.Order(), await db.Products.Select(p => p.Id).OrderBy(id => id).ToListAsync());
        var entry = Assert.Single(await db.DbToolHistory.ToListAsync());
        Assert.Equal(("0001-products", DbToolHistoryKinds.Seed), (entry.Name, entry.Kind));
    }

    [Fact]
    public async Task Seed_ConcurrentRuns_OneAppliesOthersSkip()
    {
        // Two deploys of one environment may overlap (e.g. db-update while up is seeding): none may fail.
        var cs = await postgres.CreateDatabaseAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => RunAsync(cs, [V1], SeedMode.Normal))));

        var all = results.SelectMany(r => r).ToList();
        Assert.Single(all, r => r == new SeedSetResult("0001-products", false, 2, 0));
        Assert.Equal(7, all.Count(r => r.SkippedAsApplied));
        Assert.Equal(2, await CountProductsAsync(cs));
    }

    [Fact]
    public async Task Seed_SecondRun_SkipsAppliedSet()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await RunAsync(cs, [V1], SeedMode.Normal);

        var results = await RunAsync(cs, [V1], SeedMode.Normal);

        Assert.Equal([new SeedSetResult("0001-products", true, 0, 0)], results);
        Assert.Equal(2, await CountProductsAsync(cs));
    }

    [Fact]
    public async Task Seed_DeletedProduct_IsNotRestored()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await RunAsync(cs, [V1], SeedMode.Normal);
        await ExecuteAsync(cs, $"DELETE FROM products WHERE id = '{Id1}'");

        await RunAsync(cs, [V1], SeedMode.Normal);

        Assert.Equal(1, await CountProductsAsync(cs));
    }

    [Fact]
    public async Task Restore_DeletedProduct_IsRestored()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await RunAsync(cs, [V1], SeedMode.Normal);
        await ExecuteAsync(cs, $"DELETE FROM products WHERE id = '{Id1}'");

        var results = await RunAsync(cs, [V1], SeedMode.Restore);

        Assert.Equal([new SeedSetResult("0001-products", false, 1, 1)], results);
        Assert.Equal(2, await CountProductsAsync(cs));
        await using var db = PostgresDatabaseFixture.CreateContext(cs);
        Assert.Single(await db.DbToolHistory.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Seed_And_Restore_NeverOverwriteEditedProduct(bool restore)
    {
        var cs = await postgres.CreateDatabaseAsync();
        await RunAsync(cs, [V1], SeedMode.Normal);
        await ExecuteAsync(cs, $"UPDATE products SET name = 'Edited by QA' WHERE id = '{Id1}'");

        await RunAsync(cs, [V1], restore ? SeedMode.Restore : SeedMode.Normal);

        await using var db = PostgresDatabaseFixture.CreateContext(cs);
        Assert.Equal("Edited by QA", (await db.Products.SingleAsync(p => p.Id == Id1)).Name);
    }

    [Fact]
    public async Task Restore_OnEmptyDb_AddsJournalRow()
    {
        var cs = await postgres.CreateDatabaseAsync();

        await RunAsync(cs, [V1], SeedMode.Restore);

        await using var db = PostgresDatabaseFixture.CreateContext(cs);
        Assert.Equal("0001-products", Assert.Single(await db.DbToolHistory.ToListAsync()).Name);
    }

    [Fact]
    public async Task Seed_NewSet_AppliedOldSkipped()
    {
        var cs = await postgres.CreateDatabaseAsync();
        await RunAsync(cs, [V1], SeedMode.Normal);

        var results = await RunAsync(cs, [V1, V2], SeedMode.Normal);

        Assert.Equal(
            [new SeedSetResult("0001-products", true, 0, 0), new SeedSetResult("0002-products", false, 1, 0)],
            results);
        Assert.Equal(3, await CountProductsAsync(cs));
        await using var db = PostgresDatabaseFixture.CreateContext(cs);
        Assert.Equal(2, await db.DbToolHistory.CountAsync());
    }

    [Fact]
    public async Task Seed_InvalidProduct_FailsAndWritesNothing()
    {
        var cs = await postgres.CreateDatabaseAsync();
        var invalid = new TestSeedSet("0001-products",
            () => [Product(Id1, "Notebook A5", 4.50m), Product(Id2, "Too precise", 0.001m)]);

        await Assert.ThrowsAsync<DomainValidationException>(() => RunAsync(cs, [invalid], SeedMode.Normal));

        await AssertNothingWrittenAsync(cs);
    }

    [Fact]
    public async Task Seed_DuplicateIdInSet_FailsAndWritesNothing()
    {
        var cs = await postgres.CreateDatabaseAsync();
        var duplicate = new TestSeedSet("0001-products",
            () => [Product(Id1, "Notebook A5", 4.50m), Product(Id1, "Notebook A4", 5.50m)]);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => RunAsync(cs, [duplicate], SeedMode.Normal));

        Assert.Contains(Id1.ToString(), ex.Message, StringComparison.Ordinal);
        await AssertNothingWrittenAsync(cs);
    }

    [Fact]
    public async Task Seed_ObjectThatIsNotAnEntity_FailsAndWritesNothing()
    {
        var cs = await postgres.CreateDatabaseAsync();
        var notEntity = new TestSeedSet("0001-products", () => [Product(Id1, "Notebook A5", 4.50m), "not an entity"]);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => RunAsync(cs, [notEntity], SeedMode.Normal));

        Assert.Contains(nameof(String), ex.Message, StringComparison.Ordinal);
        await AssertNothingWrittenAsync(cs);
    }

    [Fact]
    public async Task Seed_NoSets_DoesNothing()
    {
        var cs = await postgres.CreateDatabaseAsync();

        var results = await RunAsync(cs, [], SeedMode.Normal);

        Assert.Empty(results);
        await AssertNothingWrittenAsync(cs);
    }

    private static Product Product(Guid id, string name, decimal price) => Domain.Product.CreateSeeded(id, name, price, Created);

    private static async Task<IReadOnlyList<SeedSetResult>> RunAsync(string cs, IReadOnlyList<ISeedSet> sets, SeedMode mode)
    {
        await using var db = PostgresDatabaseFixture.CreateContext(cs);
        var runner = new SeedRunner(db, TimeProvider.System, NullLogger<SeedRunner>.Instance);
        return await runner.RunAsync(sets, mode, CancellationToken.None);
    }

    private static async Task<int> CountProductsAsync(string cs)
    {
        await using var db = PostgresDatabaseFixture.CreateContext(cs);
        return await db.Products.CountAsync();
    }

    private static async Task ExecuteAsync(string cs, string sql)
    {
        await using var db = PostgresDatabaseFixture.CreateContext(cs);
#pragma warning disable EF1002 // test-only SQL with constant ids
        await db.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
    }

    private static async Task AssertNothingWrittenAsync(string cs)
    {
        await using var db = PostgresDatabaseFixture.CreateContext(cs);
        Assert.Equal(0, await db.Products.CountAsync());
        Assert.Equal(0, await db.DbToolHistory.CountAsync());
    }
}
