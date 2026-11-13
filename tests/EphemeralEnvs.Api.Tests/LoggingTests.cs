using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace EphemeralEnvs.Api.Tests;

/// <summary>The API's logs: JSON lines with scopes and the request's TraceId/SpanId/ParentId (set by ServiceDefaults).</summary>
public sealed class LoggingTests
{
    private const string NoDb = "Host=localhost;Port=1;Database=x;Username=x;Password=x;Timeout=2";

    [Fact]
    public async Task Api_WritesJsonLogs_WithScopesAndTraceIds()
    {
        await using var factory = new ApiFactory(NoDb);
        var services = factory.Services;

        Assert.Equal(ConsoleFormatterNames.Json, services.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value.FormatterName);
        Assert.True(services.GetRequiredService<IOptions<JsonConsoleFormatterOptions>>().Value.IncludeScopes);
        Assert.Equal(
            ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId,
            services.GetRequiredService<IOptions<LoggerFactoryOptions>>().Value.ActivityTrackingOptions);
    }
}
