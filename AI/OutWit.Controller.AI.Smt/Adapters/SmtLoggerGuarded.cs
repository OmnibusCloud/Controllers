using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OutWit.Controller.AI.Smt.Adapters;

/// <summary>
/// The logger every Smt adapter logs through: the host's logger behind one lock, with the
/// logger's own exceptions swallowed. A logging call never decides what becomes of a package.
/// <para>
/// A host gives a controller whatever logger it has; older cloud clients gave an in-memory list
/// without a lock, and a share of several packages logs from several threads at once. The
/// adapter base logs through the logger its constructor was given, so every adapter hands it
/// this one, and the base's own lines are held to the same rule. A line the logger refused is
/// lost and counted (<see cref="Swallowed"/>); nothing else is told.
/// </para>
/// </summary>
internal sealed class SmtLoggerGuarded : ILogger
{
    #region Constants

    // One lock for the process: the adapters of a node share one logger, whatever wraps it.
    private static readonly object GATE = new();

    #endregion

    #region Fields

    private static long m_swallowed;

    private readonly ILogger m_logger;

    #endregion

    #region Constructors

    private SmtLoggerGuarded(ILogger logger)
    {
        m_logger = logger;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Puts a host's logger behind the lock. A logger that is already behind it is returned as it
    /// is; a host without a logger gets one that keeps nothing.
    /// </summary>
    /// <param name="logger">The host's logger, or null.</param>
    /// <returns>The logger an adapter logs through.</returns>
    public static ILogger Wrap(ILogger? logger)
    {
        return logger as SmtLoggerGuarded ?? new SmtLoggerGuarded(logger ?? NullLogger.Instance);
    }

    /// <summary>Ends a scope of the host's logger under the same rule as a line.</summary>
    /// <param name="scope">The scope the host's logger gave.</param>
    internal static void End(IDisposable scope)
    {
        lock (GATE)
        {
            try
            {
                scope.Dispose();
            }
            catch (Exception)
            {
                Interlocked.Increment(ref m_swallowed);
            }
        }
    }

    #endregion

    #region ILogger

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (GATE)
        {
            try
            {
                m_logger.Log(logLevel, eventId, state, exception, formatter);
            }
            catch (Exception)
            {
                // The line is lost; the work it was about is not.
                Interlocked.Increment(ref m_swallowed);
            }
        }
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        lock (GATE)
        {
            try
            {
                return m_logger.IsEnabled(logLevel);
            }
            catch (Exception)
            {
                Interlocked.Increment(ref m_swallowed);
                return false;
            }
        }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        lock (GATE)
        {
            try
            {
                var scope = m_logger.BeginScope(state);
                return scope == null ? null : new SmtLoggerGuardedScope(scope);
            }
            catch (Exception)
            {
                Interlocked.Increment(ref m_swallowed);
                return null;
            }
        }
    }

    #endregion

    #region Properties

    /// <summary>How many times the host's logger threw since the process started; the exceptions went no further.</summary>
    public static long Swallowed => Interlocked.Read(ref m_swallowed);

    #endregion
}
