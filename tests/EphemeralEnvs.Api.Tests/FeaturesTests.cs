using System.Net;
using System.Net.Http.Json;

namespace EphemeralEnvs.Api.Tests;

/// <summary>GET /features: the browser's view of the client-allowed flags (situation D). No DB needed.</summary>
public sealed class FeaturesTests
{
    private const string NoDb = "Host=localhost;Port=1;Database=x;Username=x;Password=x;Timeout=2";
    private const string WebOrigin = "http://localhost:5080";

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task Features_ReturnsAllowListedFlagState(string setting, bool expected)
    {
        await using var factory = new ApiFactory(NoDb, new Dictionary<string, string?> { ["FeatureManagement:ProductStats"] = setting });
        using var client = factory.CreateClient();

        var flags = await client.GetFromJsonAsync<Dictionary<string, bool>>("/features");

        Assert.Equal(new Dictionary<string, bool> { ["productStats"] = expected }, flags);
    }

    [Fact]
    public async Task Features_NeverExposesNonClientFlags()
    {
        await using var factory = new ApiFactory(NoDb, new Dictionary<string, string?> { ["FeatureManagement:SomeServerOnlyFlag"] = "true" });
        using var client = factory.CreateClient();

        var flags = await client.GetFromJsonAsync<Dictionary<string, bool>>("/features");

        Assert.NotNull(flags);
        Assert.Equal(["productStats"], flags.Keys);
    }

    [Fact]
    public async Task Features_FollowFlagsFileChanges_WithoutRestart()
    {
        // deploy/local mounts a flags file into the container; editing it switches flags in the running process.
        var dir = Directory.CreateTempSubdirectory("ee-flags-");
        var file = Path.Combine(dir.FullName, "flags.json");
        await File.WriteAllTextAsync(file, """{ "FeatureManagement": { "ProductStats": false } }""");
        try
        {
            await using var factory = new ApiFactory(NoDb, new Dictionary<string, string?> { ["FlagsFile"] = file });
            using var client = factory.CreateClient();
            var before = await client.GetFromJsonAsync<Dictionary<string, bool>>("/features");

            await File.WriteAllTextAsync(file, """{ "FeatureManagement": { "ProductStats": true } }""");
            var after = before;
            for (var attempt = 0; attempt < 50 && after?["productStats"] != true; attempt++)
            {
                await Task.Delay(200);
                after = await client.GetFromJsonAsync<Dictionary<string, bool>>("/features");
            }

            Assert.False(before?["productStats"]);
            Assert.True(after?["productStats"], "flags file change was not picked up within 10 s");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Features_IsNotCached()
    {
        await using var factory = new ApiFactory(NoDb);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/features", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore, $"Cache-Control: {response.Headers.CacheControl}");
    }

    [Fact]
    public async Task Features_AllowsCorsFromWeb()
    {
        await using var factory = new ApiFactory(NoDb);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/features");
        request.Headers.Add("Origin", WebOrigin);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([WebOrigin], response.Headers.GetValues("Access-Control-Allow-Origin"));
    }
}
