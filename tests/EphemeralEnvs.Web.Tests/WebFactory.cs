using System.Net;
using System.Text.RegularExpressions;

using EphemeralEnvs.Web.Services;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EphemeralEnvs.Web.Tests;

/// <summary>
/// Hosts the Web in memory. The API is replaced by <paramref name="apiHandler"/> (default: an empty product list).
/// Settings are applied via <c>UseSetting</c>, so they override appsettings.json like environment variables do.
/// </summary>
public sealed partial class WebFactory(IDictionary<string, string?>? settings = null, HttpMessageHandler? apiHandler = null)
    : WebApplicationFactory<Program>
{
    public const string ApiBaseUrl = "http://api.test/";

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Api:InternalBaseUrl", ApiBaseUrl);
        builder.UseSetting("Api:PublicBaseUrl", ApiBaseUrl);
        builder.UseSetting("App:EnvironmentName", "test");

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        var handler = apiHandler ?? new StubApiHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json"),
        });
        builder.ConfigureTestServices(services =>
            services.AddHttpClient<ProductsApiClient>().ConfigurePrimaryHttpMessageHandler(() => handler));
    }

    /// <summary>
    /// GETs the form and returns its antiforgery token and the antiforgery cookie ("name=value"), so a test can
    /// POST them itself (also to another instance).
    /// </summary>
    public static async Task<(string Token, string Cookie)> GetFormTokenAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.GetAsync(new Uri("/Home/Create", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        var token = TokenInput().Match(html);
        Assert.True(token.Success, "The form has no __RequestVerificationToken.");

        var cookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';')[0]).Single(v => v.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal))
            : throw new InvalidOperationException("No antiforgery cookie was set.");

        return (WebUtility.HtmlDecode(token.Groups[1].Value), cookie);
    }

    // <input name="__RequestVerificationToken" type="hidden" value="..." /> as rendered by the form tag helper.
    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*?value="([^"]+)"[^>]*>""")]
    private static partial Regex TokenInput();
}
