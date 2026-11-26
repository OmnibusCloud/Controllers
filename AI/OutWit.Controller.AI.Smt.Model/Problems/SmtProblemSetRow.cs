using System.Diagnostics.CodeAnalysis;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Problems;

/// <summary>
/// One row of a problem set as its reader gives it: where the row stands, and either the problem
/// it holds or why it was refused. Exactly one of the two is there.
/// </summary>
public sealed class SmtProblemSetRow
{
    #region Constructors

    internal SmtProblemSetRow(int line, int index, SmtProblemData data, SmtProblem problem)
    {
        Line = line;
        Index = index;
        Data = data;
        Problem = problem;
        IsAccepted = true;
    }

    internal SmtProblemSetRow(int line, int index, SmtRefusalData refusal)
    {
        Line = line;
        Index = index;
        Refusal = refusal;
    }

    #endregion

    #region Properties

    /// <summary>The line of the set the row stands on, from 1; blank lines are counted.</summary>
    public int Line { get; }

    /// <summary>The ordinal of the row among the rows of the set, from 0: accepted and refused rows are counted, blank lines are not.</summary>
    public int Index { get; }

    /// <summary>The problem as it travels to a node; null when the row was refused.</summary>
    public SmtProblemData? Data { get; }

    /// <summary>
    /// The problem as the sanitizer took it, for whoever wants more of it than travels: its
    /// declared constants, its logic, the commands that were stripped. Null when the row was refused.
    /// </summary>
    public SmtProblem? Problem { get; }

    /// <summary>Why the row was refused; null when it was taken.</summary>
    public SmtRefusalData? Refusal { get; }

    /// <summary>Whether the row was taken: then the problem is there, both as it travels and as the sanitizer took it, else the refusal (the constructors make it so).</summary>
    [MemberNotNullWhen(true, nameof(Data), nameof(Problem))]
    [MemberNotNullWhen(false, nameof(Refusal))]
    public bool IsAccepted { get; }

    #endregion
}
