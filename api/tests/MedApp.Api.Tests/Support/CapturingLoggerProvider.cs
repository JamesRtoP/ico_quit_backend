using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MedApp.Api.Tests.Support;

/// <summary>Captures every log line (message, state values, scopes, exception text) at all levels.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<string> _lines = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyCollection<string> Lines => _lines;

    public string AllText => string.Join('\n', _lines);

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class Logger(string category, CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var parts = new List<string> { $"[{logLevel}] {category}: {formatter(state, exception)}" };
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                parts.AddRange(values.Select(kv => $"{kv.Key}={kv.Value}"));
            }
            owner._scopes.ForEachScope((scope, list) => list.Add($"scope={scope}"), parts);
            if (exception is not null)
            {
                parts.Add(exception.ToString());
            }
            owner._lines.Enqueue(string.Join(" | ", parts));
        }
    }
}
