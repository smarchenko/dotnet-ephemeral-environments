using EphemeralEnvs.Data;

using Microsoft.Extensions.DependencyInjection;

namespace EphemeralEnvs.Application;

public static class HealthChecksBuilderExtensions
{
    /// <summary>
    /// "Can the app reach its database" check for the readiness probe. Lives here because the API
    /// cannot see <c>AppDbContext</c>; the API only decides which probe uses it (via tags).
    /// </summary>
    public static IHealthChecksBuilder AddDatabaseCheck(this IHealthChecksBuilder builder, params string[] tags) =>
        builder.AddDbContextCheck<AppDbContext>("database", tags: tags);
}
