using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// A host's logger that keeps every line it is given, in order: its level, its text and the
/// exception that came with it. What a node's own log holds, for a test that says what must be in
/// it and what must not travel further.
/// </summary>
internal sealed class SmtTestLogger : ILogger
{
    #region ILogger

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Lines.Enqueue((logLevel, formatter(state, exception), exception));
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    #endregion

    #region Properties

    /// <summary>Every line, in the order it was logged.</summary>
    public ConcurrentQueue<(LogLevel Level, string Text, Exception? Exception)> Lines { get; } = new();

    #endregion
}
