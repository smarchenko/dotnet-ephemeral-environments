using System.Net;

using EphemeralEnvs.Contracts;

namespace EphemeralEnvs.Web.Services;

/// <summary>Typed client for the API, used server-side. BaseAddress and timeout are set in Program.cs.</summary>
public sealed class ProductsApiClient(HttpClient http)
{
    /// <exception cref="HttpRequestException">API unreachable or returned a non-success status.</exception>
    /// <exception cref="TaskCanceledException">Timeout (or the request was aborted).</exception>
    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<ProductDto[]>(new Uri("products", UriKind.Relative), ct) ?? [];


    /// <summary>POST /products. 201 → created; 400 → the API's field errors; anything else throws.</summary>
    /// <exception cref="HttpRequestException">API unreachable or returned another non-success status.</exception>
    /// <exception cref="TaskCanceledException">Timeout (or the request was aborted).</exception>
    public async Task<CreateProductResult> CreateProductAsync(CreateProductRequest request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(new Uri("products", UriKind.Relative), request, ct);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(ct);
            return new CreateProductResult(false, new Dictionary<string, string[]>(problem?.Errors ?? new Dictionary<string, string[]>()));
        }

        response.EnsureSuccessStatusCode();
        return CreateProductResult.Success;
    }
}
