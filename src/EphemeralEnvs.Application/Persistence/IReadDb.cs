using EphemeralEnvs.Domain;

namespace EphemeralEnvs.Application.Persistence;

/// <summary>
/// What query handlers see of the read side: queryable sets only. No Add/Remove/SaveChanges/Database,
/// so writing does not compile. Writes that need only an IQueryable (ExecuteDelete/ExecuteUpdate)
/// are rejected by the database: the read connection is a read-only session (see AddApplication).
/// </summary>
internal interface IReadDb
{
    public IQueryable<Product> Products { get; }
}
