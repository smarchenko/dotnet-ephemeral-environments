using EphemeralEnvs.Application.Persistence;
using EphemeralEnvs.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace EphemeralEnvs.Api.Tests;

/// <summary>
/// The read side cannot write, at two levels: the context itself (no tracking, SaveChanges throws) and the
/// database session (default_transaction_read_only), which also stops ExecuteDelete/ExecuteUpdate and raw SQL.
/// Contexts come from the real DI registration (AddApplication), not built by hand.
/// </summary>
public sealed class ReadDbContextTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task SaveChanges_Throws_BecauseReadSideIsReadOnly()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadDbContext>();
        db.Add(new Product("Pen", 1m, DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Empty(await db.Products.ToArrayAsync());
    }

    [Fact]
    public async Task Queries_AreNotTracked_ByDefault()
    {
        // Guards OnConfiguring: without it, a query handler returning entities would fill the change tracker.
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadDbContext>();

        Assert.Equal(QueryTrackingBehavior.NoTracking, db.ChangeTracker.QueryTrackingBehavior);
    }

    [Fact]
    public async Task ExecuteDelete_IsRejectedByDatabase()
    {
        // Bulk operations bypass SaveChanges and need only an IQueryable, which IReadDb exposes.
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var readDb = scope.ServiceProvider.GetRequiredService<IReadDb>();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => readDb.Products.ExecuteDeleteAsync());

        Assert.Equal(PostgresErrorCodes.ReadOnlySqlTransaction, ex.SqlState);
    }

    [Fact]
    public async Task RawSql_IsRejectedByDatabase()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadDbContext>();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM products"));

        Assert.Equal(PostgresErrorCodes.ReadOnlySqlTransaction, ex.SqlState);
    }
}
