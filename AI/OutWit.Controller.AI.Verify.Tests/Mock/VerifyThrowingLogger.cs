using Microsoft.Extensions.Logging;

namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>
/// A host's logger that fails at everything it is asked: every line, every question and every
/// scope throws, with a text that would name a folder of the node if it travelled.
/// </summary>
internal sealed class VerifyThrowingLogger : ILogger
{
    #region Constants

    /// <summary>The text of every exception this logger throws: a test looks for it where it must not be.</summary>
    public const string TEXT = @"logger broke at C:\Users\someone\AppData\client\log";

    #endregion

    #region Fields

    private int m_calls;

    #endregion

    #region ILogger

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Interlocked.Increment(ref m_calls);
        throw new IndexOutOfRangeException(TEXT);
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        Interlocked.Increment(ref m_calls);
        throw new IndexOutOfRangeException(TEXT);
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        Interlocked.Increment(ref m_calls);
        throw new IndexOutOfRangeException(TEXT);
    }

    #endregion

    #region Properties

    /// <summary>How many times the logger was asked for anything.</summary>
    public int Calls => Volatile.Read(ref m_calls);

    #endregion
}
