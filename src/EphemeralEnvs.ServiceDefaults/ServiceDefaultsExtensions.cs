using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EphemeralEnvs.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// Setup shared by every service. Logs: one JSON object per line on stdout (docker logs, collectors), with scopes
    /// and the request's TraceId/SpanId/ParentId (an incoming W3C <c>traceparent</c> is continued).
    /// Stage O1 adds OpenTelemetry here.
    /// </summary>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
        builder.Logging.Configure(o => o.ActivityTrackingOptions =
            ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId);

        return builder;
    }
}
