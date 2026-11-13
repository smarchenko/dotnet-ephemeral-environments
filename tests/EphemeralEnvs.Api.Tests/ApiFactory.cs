using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EphemeralEnvs.Api.Tests;

/// <summary>
/// Hosts the API in memory against the given database. Settings are applied via <c>UseSetting</c>,
/// so they override appsettings.json the same way environment variables do in a container.
/// <paramref name="configureServices"/> can replace services (e.g. a handler) for a single test.
/// </summary>
public sealed class ApiFactory(
    string connectionString,
    IDictionary<string, string?>? extra = null,
    Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Cors:AllowedOrigins", "http://localhost:5080");
        builder.UseSetting("Swagger:Enabled", "true");

        foreach (var (key, value) in extra ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        if (configureServices is not null)
        {
            builder.ConfigureTestServices(configureServices);
        }
    }
}
