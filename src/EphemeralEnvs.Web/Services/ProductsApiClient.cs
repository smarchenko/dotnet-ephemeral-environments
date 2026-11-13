using EphemeralEnvs.Contracts;

namespace EphemeralEnvs.Web.Services;

/// <summary>Typed client for the API, used server-side. BaseAddress and timeout are set in Program.cs.</summary>
public sealed class ProductsApiClient(HttpClient http)
{
    /// <exception cref="HttpRequestException">API unreachable or returned a non-success status.</exception>
    /// <exception cref="TaskCanceledException">Timeout (or the request was aborted).</exception>
    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<ProductDto[]>(new Uri("products", UriKind.Relative), ct) ?? [];
}
