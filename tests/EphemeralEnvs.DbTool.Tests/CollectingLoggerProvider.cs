using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EphemeralEnvs.DbTool.Tests;

/// <summary>Keeps the formatted messages of every error logged by the tool, so tests can check what the user reads.</summary>
internal sealed class CollectingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _errors = new();

    public IReadOnlyCollection<string> Errors => _errors;

    /// <summary>Hook for <see cref="DbToolCli.RunAsync"/>: adds this provider to the tool's host.</summary>
    public void AddTo(IHostApplicationBuilder builder) => builder.Services.AddSingleton<ILoggerProvider>(this);

    public ILogger CreateLogger(string categoryName) => new Logger(_errors);

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentQueue<string> errors) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                errors.Enqueue(formatter(state, exception));
            }
        }
    }
}
