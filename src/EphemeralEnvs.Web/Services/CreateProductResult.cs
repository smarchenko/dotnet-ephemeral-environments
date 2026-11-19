namespace EphemeralEnvs.Web.Services;

/// <summary>
/// Outcome of <see cref="ProductsApiClient.CreateProductAsync"/>: created, or rejected by the API's validation with
/// the field errors of its ValidationProblemDetails (field name → messages).
/// </summary>
public sealed record CreateProductResult(bool Created, IReadOnlyDictionary<string, string[]> Errors)
{
    public static readonly CreateProductResult Success = new(true, new Dictionary<string, string[]>());
}
