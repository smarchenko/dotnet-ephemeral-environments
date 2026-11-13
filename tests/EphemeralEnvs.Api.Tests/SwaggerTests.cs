using System.Net;
using System.Text.Json;

using EphemeralEnvs.Contracts;

namespace EphemeralEnvs.Api.Tests;

/// <summary>Swagger is switched by the Swagger:Enabled flag, not by the ASP.NET Core environment. No DB needed.</summary>
public sealed class SwaggerTests
{
    private const string NoDb = "Host=localhost;Port=1;Database=x;Username=x;Password=x;Timeout=2";

    [Fact]
    public async Task Enabled_ServesDocWithEnvironmentName()
    {
        await using var factory = new ApiFactory(NoDb, new Dictionary<string, string?>
        {
            ["Swagger:Enabled"] = "true",
            ["App:EnvironmentName"] = "test-env",
        });
        using var client = factory.CreateClient();

        var doc = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        var ui = await client.GetAsync(new Uri("/swagger/index.html", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, doc.StatusCode);
        Assert.Contains("Environment: test-env", await doc.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
    }

    [Fact]
    public async Task Doc_ShowsRequestLimits()
    {
        // Clients (and Swagger UI) see the same limits the API validates: [ProductName] and [Price].
        await using var factory = new ApiFactory(NoDb, new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" });
        using var client = factory.CreateClient();

        using var doc = JsonDocument.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)));
        var properties = doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(nameof(CreateProductRequest)).GetProperty("properties");

        Assert.Equal(ProductNameAttribute.MaxLength, properties.GetProperty("name").GetProperty("maxLength").GetInt32());
        Assert.Equal(0.01m, properties.GetProperty("price").GetProperty("minimum").GetDecimal());
        Assert.Equal(9_999_999_999_999_999.99m, properties.GetProperty("price").GetProperty("maximum").GetDecimal());
    }

    [Fact]
    public async Task Disabled_Returns404()
    {
        await using var factory = new ApiFactory(NoDb, new Dictionary<string, string?> { ["Swagger:Enabled"] = "false" });
        using var client = factory.CreateClient();

        var doc = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        var ui = await client.GetAsync(new Uri("/swagger/index.html", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, doc.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ui.StatusCode);
    }
}
