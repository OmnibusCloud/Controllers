using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OutWit.Controller.AI.Corpus.Logging;

/// <summary>
/// The one way Corpus writes to a host's log. A host's logger is the host's own: on a node it can
/// be an in-memory list without a lock (the engine's default one, which clients up to 2.2.5 hand
/// to controllers), which throws when two shards log at once and, once broken, refuses every later
/// line. A line that cannot be written must not decide what happens to a shard, a package or a
/// job, so every call goes through here: one at a time for the whole controller in this process,
/// and whatever the host's logger throws ends here, counted and otherwise ignored.
/// <para>
/// Every adapter hands the engine's adapter base this logger instead of the host's, so the
/// base's own lines (it logs inside its catch block before it returns a status) are covered too:
/// with a logger that throws there, the bare exception would leave the node past the fixed words
/// a result carries.
/// </para>
/// </summary>
internal sealed class CorpusLogger : ILogger
{
    #region Constants

    /// <summary>One gate for the controller in this process: the host's logger is one object whichever adapter writes to it.</summary>
    private static readonly Lock GATE = new();

    #endregion

    #region Fields

    private static long m_lost;

    private readonly ILogger m_inner;

    #endregion

    #region Constructors

    private CorpusLogger(ILogger inner)
    {
        m_inner = inner;
    }

    #endregion

    #region Functions

    /// <summary>The logger Corpus writes through, over a host's logger.</summary>
    /// <param name="logger">The host's logger; null for none.</param>
    /// <returns>The guarded logger; the argument itself when it already is one.</returns>
    public static ILogger Wrap(ILogger? logger)
    {
        return logger as CorpusLogger ?? new CorpusLogger(logger ?? NullLogger.Instance);
    }

    /// <summary>Counts a call the host's logger failed.</summary>
    internal static void Lose()
    {
        Interlocked.Increment(ref m_lost);
    }

    #endregion

    #region ILogger

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            lock (GATE)
            {
                m_inner.Log(logLevel, eventId, state, exception, formatter);
            }
        }
        catch (Exception)
        {
            // The log's own failure, whatever it is: the line is lost, the work goes on.
            Lose();
        }
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        try
        {
            lock (GATE)
            {
                return m_inner.IsEnabled(logLevel);
            }
        }
        catch (Exception)
        {
            Lose();
            return false;
        }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        try
        {
            lock (GATE)
            {
                var scope = m_inner.BeginScope(state);
                return scope == null ? null : new CorpusLoggerScope(scope, GATE);
            }
        }
        catch (Exception)
        {
            Lose();
            return null;
        }
    }

    #endregion

    #region Properties

    /// <summary>How many calls the host's logger failed in this process: lines that were lost, and nothing else.</summary>
    internal static long Lost => Interlocked.Read(ref m_lost);

    #endregion
}
