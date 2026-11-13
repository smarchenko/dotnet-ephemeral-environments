namespace EphemeralEnvs.Contracts;

public sealed record ProductDto(Guid Id, string Name, decimal Price, DateTimeOffset CreatedAt);
