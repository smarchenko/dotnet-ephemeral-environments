using EphemeralEnvs.Data.History;
using EphemeralEnvs.Domain;

using Microsoft.EntityFrameworkCore;

namespace EphemeralEnvs.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    /// <summary>Journal of applied DbTool operations (seed sets; backfills later).</summary>
    internal DbSet<DbToolHistoryEntry> DbToolHistory => Set<DbToolHistoryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
