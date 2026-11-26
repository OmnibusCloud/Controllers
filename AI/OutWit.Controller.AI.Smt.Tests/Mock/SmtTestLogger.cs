using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// A host's logger that keeps every line it is given, in order: its level, its text and the
/// exception that came with it. What a node's own log holds, for a test that says what must be in
/// it and what must not travel further; and a test can wait for a line instead of for a time.
/// </summary>
internal sealed class SmtTestLogger : ILogger
{
    #region Events

    /// <summary>A line was logged; raised on the thread that logged it, after the line is kept.</summary>
    public event Action<LogLevel, string>? Logged;

    #endregion

    #region Functions

    /// <summary>A task that ends when a warning is logged from now on: the moment a transfer says that an attempt of it did not go well.</summary>
    /// <returns>The task.</returns>
    public Task NextWarning()
    {
        return Warnings(1);
    }

    /// <summary>A task that ends when so many warnings have been logged from now on.</summary>
    /// <param name="count">The warnings.</param>
    /// <returns>The task.</returns>
    public Task Warnings(int count)
    {
        var logged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var left = count;
        Logged += (level, _) =>
        {
            if (level == LogLevel.Warning && Interlocked.Decrement(ref left) == 0)
                logged.TrySetResult();
        };

        return logged.Task;
    }

    #endregion

    #region ILogger

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var text = formatter(state, exception);
        Lines.Enqueue((logLevel, text, exception));
        Logged?.Invoke(logLevel, text);
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
