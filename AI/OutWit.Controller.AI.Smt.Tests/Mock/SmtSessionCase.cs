using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// One way a problem's session on a node can end, as a script (<see cref="SmtSessionScript"/>):
/// what the solver answers to each thing the session sends, how its run then ended, and what the
/// row a node makes of it must say in a few words. A case without a session is a problem the
/// solver's entry answers before any session starts: a refusal, or a fault of the node.
/// </summary>
/// <param name="Id">The case's name, once in <see cref="SmtSessionCases"/>.</param>
/// <param name="Problem">The problem as it travels.</param>
/// <param name="Options">The run's options.</param>
/// <param name="Answers">What the solver writes after each thing the session sends, in order.</param>
/// <param name="Expected">The row in words, as <see cref="SmtNodeRig.Words"/> writes it (<c>ok sat</c>, <c>ok sat fuel 1 incomplete</c>, <c>engine_crash trap</c>).</param>
internal sealed record SmtSessionCase(string Id, SmtProblemData Problem, SmtOptionsData Options, IReadOnlyList<string> Answers, string Expected)
{
    #region Functions

    /// <summary>The case's name, as a test's log shows it.</summary>
    /// <returns>The id.</returns>
    public override string ToString()
    {
        return Id;
    }

    #endregion

    #region Properties

    /// <summary>How the run ended; null for a solver that ended by itself with status 0.</summary>
    public SmtScriptedEnd? End { get; init; }

    /// <summary>The session is cancelled before the read that follows this many things sent; -1 for a session nobody cancels.</summary>
    public int CancelAfter { get; init; } = -1;

    /// <summary>Whether no session runs: the problem goes to the solver's entry on a node without a sandbox, which refuses it or says that it has none.</summary>
    public bool NoSession { get; init; }

    #endregion
}
