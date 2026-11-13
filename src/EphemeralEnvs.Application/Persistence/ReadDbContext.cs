using EphemeralEnvs.Data;
using EphemeralEnvs.Domain;

using Microsoft.EntityFrameworkCore;

namespace EphemeralEnvs.Application.Persistence;

/// <summary>
/// Read side of CQRS: read-only access to the same tables as <see cref="AppDbContext"/> (same mappings),
/// no change tracking, saving is not allowed. Queries project straight into DTOs.
/// Lives here, not in Data, so `dotnet ef` sees only one context (the one that owns the migrations).
/// </summary>
internal sealed class ReadDbContext(DbContextOptions<ReadDbContext> options) : DbContext(options), IReadDb
{
    public IQueryable<Product> Products => Set<Product>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new InvalidOperationException("ReadDbContext is read-only. Use a command (AppDbContext) to change data.");

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("ReadDbContext is read-only. Use a command (AppDbContext) to change data.");

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
