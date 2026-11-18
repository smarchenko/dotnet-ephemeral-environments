using EphemeralEnvs.Data;
using EphemeralEnvs.DbTool.Seeding;
using EphemeralEnvs.ServiceDefaults;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace EphemeralEnvs.DbTool;

/// <summary>
/// Entry point of the tool. Commands: <c>seed</c>, <c>seed --restore</c> (stage 4c adds <c>backfill</c>).
/// Config (env vars): <c>ConnectionStrings__Default</c>, <c>App__EnvironmentName</c>. Seed sets: <c>Seeding/Sets</c> (C# classes).
/// Exit codes: 0 success, 1 failure, 2 usage error.
/// </summary>
public static partial class DbToolCli
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int UsageError = 2;

    private const string Usage = """
        Usage: EphemeralEnvs.DbTool seed [--restore]
          seed            apply seed sets that are not applied to this database yet
          seed --restore  also re-insert seed data that was deleted (never overwrites changed rows)
        Config: ConnectionStrings__Default, App__EnvironmentName (required, not production/prod)
        """;

    // PostgreSQL "undefined_table" / "undefined_column": the schema is not migrated or is behind the seed code.
    private const string UndefinedTable = "42P01";
    private const string UndefinedColumn = "42703";

    /// <param name="args">Command-line arguments (commands only; config comes from env vars).</param>
    /// <param name="configOverrides">Extra config on top of env vars (tests).</param>
    /// <param name="configureHost">Extra host setup after the defaults, e.g. a log collector (tests).</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<int> RunAsync(
        string[] args,
        IReadOnlyDictionary<string, string?>? configOverrides = null,
        Action<IHostApplicationBuilder>? configureHost = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!TryParse(args, out var mode))
        {
            await Console.Error.WriteLineAsync(Usage);
            return UsageError;
        }

        // Args are not passed to the host: they are commands, not configuration.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
        if (configOverrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(configOverrides);
        }

        builder.AddServiceDefaults();

        // Same registration style as the API: EF Core gets the host's logging. The connection string is read lazily,
        // so a missing one is reported below (with a log line) before any DbContext is resolved.
        builder.Services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<SeedRunner>();
        configureHost?.Invoke(builder);

        using var host = builder.Build();
        var config = host.Services.GetRequiredService<IConfiguration>();
        // DbToolCli is static, so ILogger<DbToolCli> is not possible; the category name is the same.
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbToolCli));

        var environmentName = config["App:EnvironmentName"];
        if (!SeedGuard.IsAllowed(environmentName))
        {
            LogGuardRefused(logger, environmentName ?? "(not set)");
            return Failure;
        }

        var connectionString = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            LogNoConnectionString(logger);
            return Failure;
        }

        try
        {
            var sets = SeedSetCatalog.Discover(typeof(DbToolCli).Assembly);

            await using var scope = host.Services.CreateAsyncScope();
            var runner = scope.ServiceProvider.GetRequiredService<SeedRunner>();
            await runner.RunAsync(sets, mode, ct);

            LogDone(logger, environmentName!);
            return Success;
        }
        catch (PostgresException ex) when (ex.SqlState is UndefinedTable or UndefinedColumn)
        {
            LogSchemaMissing(logger, ex);
            return Failure;
        }
#pragma warning disable CA1031 // A deploy step must turn any failure into a non-zero exit code with a log line.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(logger, ex);
            return Failure;
        }
    }

    private static bool TryParse(string[] args, out SeedMode mode)
    {
        mode = SeedMode.Normal;
        switch (args)
        {
            case ["seed"]:
                return true;
            case ["seed", "--restore"]:
                mode = SeedMode.Restore;
                return true;
            default:
                return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Seeding refused: App__EnvironmentName is '{EnvironmentName}'. Set a non-production environment name (production/prod are never seeded)")]
    private static partial void LogGuardRefused(ILogger logger, string environmentName);

    [LoggerMessage(Level = LogLevel.Error, Message = "ConnectionStrings__Default is not set")]
    private static partial void LogNoConnectionString(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Seed failed: the schema is missing or out of date. Run the migrations (efbundle) before seeding")]
    private static partial void LogSchemaMissing(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Seed failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seed finished for environment {EnvironmentName}")]
    private static partial void LogDone(ILogger logger, string environmentName);
}
