namespace EphemeralEnvs.Contracts;

/// <param name="Name">Required, not whitespace-only, at most 200 characters (column <c>varchar(200)</c>).</param>
/// <param name="Price">
/// From 0.01 up to the largest value that fits the column <c>numeric(18,2)</c>, at most 2 decimal places.
/// </param>
public sealed record CreateProductRequest([ProductName] string Name, [Price] decimal Price);
