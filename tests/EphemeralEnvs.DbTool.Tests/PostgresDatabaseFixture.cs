using EphemeralEnvs.Data;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Testcontainers.PostgreSql;

namespace EphemeralEnvs.DbTool.Tests;

/// <summary>
/// One throwaway PostgreSQL 17 container for all DbTool tests; every test gets its own fresh database
/// (<see cref="CreateDatabaseAsync"/>), because seeding changes data.
/// </summary>
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    // Same Linux-engine selection as EphemeralEnvs.Api.Tests.PostgresFixture (Docker Desktop with two engines).
    private const string DockerDesktopLinuxEnginePipe = "dockerDesktopLinuxEngine";

    private readonly PostgreSqlContainer _container = CreateBuilder().Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates an empty database and, unless <paramref name="migrate"/> is false, applies all migrations.</summary>
    /// <returns>Connection string of the new database.</returns>
    public async Task<string> CreateDatabaseAsync(bool migrate = true)
    {
        var name = $"test_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;

        if (migrate)
        {
            await using var db = CreateContext(connectionString);
            await db.Database.MigrateAsync();
        }

        return connectionString;
    }

    public static AppDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);

    private static PostgreSqlBuilder CreateBuilder()
    {
        var builder = new PostgreSqlBuilder("postgres:17");

        var useLinuxEnginePipe = OperatingSystem.IsWindows()
            && Environment.GetEnvironmentVariable("DOCKER_HOST") is null
            && File.Exists($@"\\.\pipe\{DockerDesktopLinuxEnginePipe}");

        return useLinuxEnginePipe
            ? builder.WithDockerEndpoint($"npipe://./pipe/{DockerDesktopLinuxEnginePipe}")
            : builder;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresGroup : ICollectionFixture<PostgresDatabaseFixture>
{
    public const string Name = "postgres";
}
