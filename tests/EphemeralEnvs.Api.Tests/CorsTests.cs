using System.Net;

using EphemeralEnvs.Api.Infrastructure;

namespace EphemeralEnvs.Api.Tests;

/// <summary>ApiFactory allows the origin http://localhost:5080 (as the Web in compose).</summary>
public sealed class CorsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string AllowedOrigin = "http://localhost:5080";
    private const string AllowOriginHeader = "Access-Control-Allow-Origin";

    [Fact]
    public async Task AllowedOrigin_GetsAllowOriginHeader()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/products");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([AllowedOrigin], response.Headers.GetValues(AllowOriginHeader));
    }

    [Fact]
    public async Task UnknownOrigin_GetsNoAllowOriginHeader()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/products");
        request.Headers.Add("Origin", "http://evil.localhost");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains(AllowOriginHeader));
    }

    [Fact]
    public async Task Preflight_ForJsonPost_FromAllowedOrigin_Succeeds()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/products");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([AllowedOrigin], response.Headers.GetValues(AllowOriginHeader));
    }

    [Fact]
    public void ParseOrigins_TrimsSpacesAndTrailingSlash()
    {
        Assert.Equal(
            ["http://localhost:5080", "http://a.localhost:8080"],
            CorsSetup.ParseOrigins(" http://localhost:5080/ , http://a.localhost:8080"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ")]
    public void ParseOrigins_Empty_ReturnsNoOrigins(string? raw)
    {
        Assert.Empty(CorsSetup.ParseOrigins(raw));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("http://localhost:5080,*")]
    public void ParseOrigins_Wildcard_Throws(string raw)
    {
        Assert.Throws<InvalidOperationException>(() => CorsSetup.ParseOrigins(raw));
    }
}
