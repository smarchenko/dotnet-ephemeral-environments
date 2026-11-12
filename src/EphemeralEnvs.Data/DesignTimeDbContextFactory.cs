using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EphemeralEnvs.Data;

/// <summary>
/// Used only by the <c>dotnet ef</c> tooling (migrations add / database update).
/// Connection string: env var <c>EE_DB</c>, or the local docker-compose database by default.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=ephemeral_envs;Username=ephemeral_envs;Password=ephemeral_envs";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("EE_DB") ?? DefaultConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        return new AppDbContext(options);
    }
}
