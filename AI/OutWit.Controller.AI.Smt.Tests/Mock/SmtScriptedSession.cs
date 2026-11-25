namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>One session of a scripted solver (<see cref="SmtScriptedSolver"/>): whose it was, when it ran, how it ended.</summary>
internal sealed class SmtScriptedSession
{
    #region Properties

    /// <summary>The ticket of the problem's package.</summary>
    public required string Ticket { get; init; }

    /// <summary>The problem's id.</summary>
    public required string Id { get; init; }

    /// <summary>The memory the session's package counts for a session, bytes.</summary>
    public required long SessionBytes { get; init; }

    /// <summary>When the session started, from the solver's making.</summary>
    public required TimeSpan Start { get; init; }

    /// <summary>When it ended; null while it runs.</summary>
    public TimeSpan? End { get; set; }

    /// <summary>The sessions running when it started, itself among them.</summary>
    public required int RunningAtStart { get; init; }

    /// <summary>Whether the session ended because its package was cancelled.</summary>
    public bool Cancelled { get; set; }

    /// <summary>Whether a held session ended because nobody let it go in time: a test that waited for the wrong thing.</summary>
    public bool TimedOut { get; set; }

    #endregion
}
