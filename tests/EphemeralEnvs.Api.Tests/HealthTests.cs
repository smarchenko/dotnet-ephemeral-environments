using System.Net;

namespace EphemeralEnvs.Api.Tests;

public sealed class HealthTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string UnreachableDb = "Host=localhost;Port=1;Database=x;Username=x;Password=x;Timeout=2";

    [Fact]
    public async Task Live_Returns200()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_WithDb_Returns200()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_WithUnreachableDb_Returns503_AndLiveStays200()
    {
        // Liveness must not depend on the DB: a restart would not fix a DB outage.
        await using var factory = new ApiFactory(UnreachableDb);
        using var client = factory.CreateClient();

        var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }
}
