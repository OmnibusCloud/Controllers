using Microsoft.Extensions.Logging;

namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>A host's logger that keeps no line and whose scopes throw when they end.</summary>
internal sealed class VerifyScopeLogger : ILogger
{
    #region ILogger

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return new VerifyThrowingScope();
    }

    #endregion
}
