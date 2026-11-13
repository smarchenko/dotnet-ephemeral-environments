using EphemeralEnvs.Data;

using Microsoft.EntityFrameworkCore;

using Testcontainers.PostgreSql;

namespace EphemeralEnvs.Api.Tests;

/// <summary>
/// One throwaway PostgreSQL 17 container per test class, with all migrations applied once.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Docker Desktop on Windows can run a Windows and a Linux engine side by side; the default pipe
    // (docker_engine) points at whichever is active. Always use the Linux engine when it exists.
    // Elsewhere (Linux CI, macOS) Testcontainers' auto-detection is used.
    private const string DockerDesktopLinuxEnginePipe = "dockerDesktopLinuxEngine";

    private readonly PostgreSqlContainer _container = CreateBuilder().Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

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
