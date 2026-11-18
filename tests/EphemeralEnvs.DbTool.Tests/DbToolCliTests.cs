using Microsoft.EntityFrameworkCore;

namespace EphemeralEnvs.DbTool.Tests;

/// <summary>The CLI runs the seed sets shipped in the DbTool assembly (today: <c>0001-products</c>, 5 products).</summary>
[Collection(PostgresGroup.Name)]
public sealed class DbToolCliTests(PostgresDatabaseFixture postgres)
{
    private const int ShippedProducts = 5;

    [Fact]
    public async Task Cli_Seed_Returns0_AndSeeds()
    {
        var cs = await postgres.CreateDatabaseAsync();

        var exitCode = await DbToolCli.RunAsync(["seed"], Config(cs));

        Assert.Equal(DbToolCli.Success, exitCode);
        await using var db = PostgresDatabaseFixture.CreateContext(cs);
        Assert.Equal(ShippedProducts, await db.Products.CountAsync());
        Assert.Equal("0001-products", (await db.DbToolHistory.SingleAsync()).Name);
    }

    [Fact]
    public async Task Cli_SeedRestore_EmptyDb_Returns0_AndSeeds()
    {
        var cs = await postgres.CreateDatabaseAsync();

        var exitCode = await DbToolCli.RunAsync(["seed", "--restore"], Config(cs));

        Assert.Equal(DbToolCli.Success, exitCode);
        await using var db = PostgresDatabaseFixture.CreateContext(cs);
        Assert.Equal(ShippedProducts, await db.Products.CountAsync());
        Assert.Equal("0001-products", (await db.DbToolHistory.SingleAsync()).Name);
    }

    [Theory]
    [InlineData]
    [InlineData("migrate")]
    [InlineData("seed", "--force")]
    public async Task Cli_UnknownCommand_Returns2(params string[] args)
    {
        Assert.Equal(DbToolCli.UsageError, await DbToolCli.RunAsync(args, Config("Host=unused")));
    }

    [Theory]
    [InlineData("production")]
    [InlineData("")]
    public async Task Cli_GuardRefuses_Returns1_AndWritesNothing(string environmentName)
    {
        var cs = await postgres.CreateDatabaseAsync();

        var exitCode = await DbToolCli.RunAsync(["seed"], Config(cs, environmentName));

        Assert.Equal(DbToolCli.Failure, exitCode);
        await using var db = PostgresDatabaseFixture.CreateContext(cs);
        Assert.Equal(0, await db.Products.CountAsync());
    }

    [Fact]
    public async Task Cli_NoConnectionString_Returns1()
    {
        Assert.Equal(DbToolCli.Failure, await DbToolCli.RunAsync(["seed"], Config(connectionString: null)));
    }

    [Fact]
    public async Task Cli_SchemaMissing_Returns1_AndSaysRunMigrations()
    {
        var cs = await postgres.CreateDatabaseAsync(migrate: false);
        var logs = new CollectingLoggerProvider();

        Assert.Equal(DbToolCli.Failure, await DbToolCli.RunAsync(["seed"], Config(cs), logs.AddTo));

        Assert.Contains(logs.Errors, m => m.Contains("Run the migrations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cli_SchemaBehind_Returns1_AndSaysRunMigrations()
    {
        // The database misses the latest migration: the table exists, a column does not (42703).
        var cs = await postgres.CreateDatabaseAsync();
        await using (var db = PostgresDatabaseFixture.CreateContext(cs))
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE products DROP COLUMN price");
        }

        var logs = new CollectingLoggerProvider();

        Assert.Equal(DbToolCli.Failure, await DbToolCli.RunAsync(["seed"], Config(cs), logs.AddTo));

        Assert.Contains(logs.Errors, m => m.Contains("Run the migrations", StringComparison.Ordinal));
    }

    private static Dictionary<string, string?> Config(string? connectionString, string environmentName = "test") => new()
    {
        ["ConnectionStrings:Default"] = connectionString,
        ["App:EnvironmentName"] = environmentName,
    };
}
