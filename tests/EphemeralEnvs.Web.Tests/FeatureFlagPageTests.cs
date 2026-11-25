using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using EphemeralEnvs.Contracts;
using EphemeralEnvs.Web.Controllers;

namespace EphemeralEnvs.Web.Tests;

/// <summary>
/// Server-side flags on the products page: NewBadge (situation C, the view decides) and the stats panel
/// (situation E, the Web's flag is on but the API may still say 404).
/// </summary>
public sealed partial class FeatureFlagPageTests
{
    private const string NotAvailable = "Statistics are not available (feature off in the API).";
    private const string Unavailable = "Statistics are unavailable right now.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly ProductDto[] Products =
    [
        new(Guid.NewGuid(), "Fresh", 1.00m, DateTimeOffset.UtcNow.AddHours(-1)),
        new(Guid.NewGuid(), "Old", 4.00m, DateTimeOffset.UtcNow.AddHours(-48)),
    ];

    [Fact]
    public async Task NewBadge_On_MarksOnlyRecentProducts()
    {
        var html = await GetIndex(Api(), ("NewBadge", "true"));

        var rows = Rows(html);
        Assert.Single(rows, row => row.Contains(">New<", StringComparison.Ordinal));
        Assert.Contains(">New<", rows.Single(row => row.Contains("Fresh", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewBadge_Off_NoBadge()
    {
        var html = await GetIndex(Api(), ("NewBadge", "false"));

        Assert.Contains("Fresh", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">New<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stats_WebFlagOff_DoesNotCallStatsEndpoint()
    {
        var api = Api();

        await GetIndex(api, ("ProductStats", "false"));

        Assert.DoesNotContain(api.Requests, r => r.Path == "/products/stats");
    }

    [Fact]
    public async Task Stats_On_RendersPanel()
    {
        var stats = new ProductStatsDto(2, 2.50m, Products[0].CreatedAt);

        var html = await GetIndex(Api(stats: _ => JsonResponse(stats)), ("ProductStats", "true"));

        Assert.Contains("2 products", html, StringComparison.Ordinal);
        Assert.Contains("2.50", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stats_On_Api404_ShowsNotAvailable_AndProducts()
    {
        var html = await GetIndex(Api(stats: _ => new HttpResponseMessage(HttpStatusCode.NotFound)), ("ProductStats", "true"));

        AssertPageWithProducts(html, NotAvailable);
    }

    [Fact]
    public async Task Stats_On_Api500_ShowsUnavailable_AndProducts()
    {
        var html = await GetIndex(Api(stats: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)), ("ProductStats", "true"));

        AssertPageWithProducts(html, Unavailable);
    }

    [Fact]
    public async Task Stats_On_Timeout_ShowsUnavailable_AndProducts()
    {
        var html = await GetIndex(Api(stats: _ => throw new TaskCanceledException("timeout")), ("ProductStats", "true"));

        AssertPageWithProducts(html, Unavailable);
    }

    [Fact]
    public async Task Products_Unreachable_StillShowsApiUnavailable()
    {
        var html = await GetIndex(StubApiHandler.Unreachable(), ("ProductStats", "true"), ("NewBadge", "true"));

        Assert.Contains(HomeController.ApiUnavailableMessage, html, StringComparison.Ordinal);
    }

    private static void AssertPageWithProducts(string html, string statsMessage)
    {
        Assert.Contains(statsMessage, html, StringComparison.Ordinal);
        Assert.Contains("Fresh", html, StringComparison.Ordinal);
        Assert.Contains("Old", html, StringComparison.Ordinal);
        Assert.DoesNotContain(HomeController.ApiUnavailableMessage, html, StringComparison.Ordinal);
    }

    private static async Task<string> GetIndex(StubApiHandler api, params (string Flag, string Value)[] flags)
    {
        var settings = flags.ToDictionary(f => $"FeatureManagement:{f.Flag}", f => (string?)f.Value);
        await using var factory = new WebFactory(settings, api);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>GET /products → <see cref="Products"/>; GET /products/stats → <paramref name="stats"/> (default 404).</summary>
    private static StubApiHandler Api(Func<HttpRequestMessage, HttpResponseMessage>? stats = null) => new(request =>
        request.RequestUri!.AbsolutePath == "/products/stats"
            ? (stats ?? (_ => new HttpResponseMessage(HttpStatusCode.NotFound)))(request)
            : JsonResponse(Products));

    private static HttpResponseMessage JsonResponse<T>(T body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json"),
    };

    private static List<string> Rows(string html) => TableRow().Matches(html).Select(m => m.Value).ToList();

    [GeneratedRegex("<tr>.*?</tr>", RegexOptions.Singleline)]
    private static partial Regex TableRow();
}
