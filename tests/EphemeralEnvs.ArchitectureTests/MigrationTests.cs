using EphemeralEnvs.Data;

using Microsoft.EntityFrameworkCore;

namespace EphemeralEnvs.ArchitectureTests;

public sealed class MigrationTests
{
    [Fact]
    public void Model_HasNoChangesWithoutMigration()
    {
        // The database is built only from migrations: a model change (entity, mapping) without
        // `dotnet ef migrations add` would never reach any environment. Compares the model with the
        // migrations' snapshot; no database is contacted (the connection string is never opened).
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=unused").Options);

        Assert.False(
            db.Database.HasPendingModelChanges(),
            "The EF model has changes that no migration contains. Run: dotnet ef migrations add <Name> --project src/EphemeralEnvs.Data");
    }
}
