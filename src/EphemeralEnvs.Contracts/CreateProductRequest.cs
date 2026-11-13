namespace EphemeralEnvs.Contracts;

/// <remarks>
/// Properties, not a positional record: attributes on constructor parameters are validated by MVC but ignored by
/// the OpenAPI generator, so the schema would lose maxLength/minimum/maximum.
/// </remarks>
public sealed record CreateProductRequest
{
    /// <summary>Required, not whitespace-only, at most 200 characters (column <c>varchar(200)</c>).</summary>
    [ProductName]
    public string Name { get; init; } = string.Empty;

    /// <summary>From 0.01 up to the largest value that fits the column <c>numeric(18,2)</c>, at most 2 decimal places.</summary>
    [Price]
    public decimal Price { get; init; }
}
