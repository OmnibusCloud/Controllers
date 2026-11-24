using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>How one problem was settled.</summary>
/// <param name="Index">The problem.</param>
/// <param name="Accepted">The copy whose row is the problem's answer, or null when it has none.</param>
/// <param name="Check">What the host's evaluator said of the accepted row's models.</param>
/// <param name="HostStatus">For a problem without an answer, <see cref="SmtRunWords.NO_RESULT"/> or <see cref="SmtRunWords.DISPUTED"/>; empty otherwise.</param>
/// <param name="Reason">For a problem without an answer, the code that says why; empty otherwise.</param>
/// <param name="Copies">The answers the problem was ruled on: of the first fan-out, of the repair round for a problem it answered, and of the dispute round.</param>
/// <param name="Agreed">Whether all of them gave one digest.</param>
/// <param name="SettledBy">How the answer was settled (<see cref="SmtRunWords.SETTLED_BY"/>); empty without one.</param>
/// <param name="Repaired">Whether the repair round's answers are among those the problem was first ruled on (for a problem without any answer: always).</param>
/// <param name="Dispute">The record of the problem's dispute, or null when it had none.</param>
/// <param name="Lies">The answers that differ from the settled answer of the others.</param>
internal sealed record SmtRuling(int Index, SmtCopy? Accepted, SmtModelCheck Check, string HostStatus, string Reason, int Copies, bool Agreed, string SettledBy,
    bool Repaired, SmtDisputeRecord? Dispute, int Lies)
{
    #region Functions

    /// <summary>A problem with an answer.</summary>
    /// <param name="index">The problem.</param>
    /// <param name="accepted">The copy.</param>
    /// <param name="check">What the evaluator said of it.</param>
    /// <param name="copies">The answers it was ruled on.</param>
    /// <param name="agreed">Whether they all gave one digest.</param>
    /// <param name="settledBy">How.</param>
    /// <param name="repaired">Whether the repair round's answers are among those it was first ruled on.</param>
    /// <param name="dispute">Its dispute, or null.</param>
    /// <param name="lies">The answers that differ from the settled one.</param>
    /// <returns>The ruling.</returns>
    public static SmtRuling Accept(int index, SmtCopy accepted, SmtModelCheck check, int copies, bool agreed, string settledBy, bool repaired, SmtDisputeRecord? dispute = null, int lies = 0)
    {
        return new SmtRuling(index, accepted, check, "", "", copies, agreed, settledBy, repaired, dispute, lies);
    }

    /// <summary>A problem no node answered.</summary>
    /// <param name="index">The problem.</param>
    /// <param name="reason">What the nodes said last.</param>
    /// <returns>The ruling.</returns>
    public static SmtRuling Without(int index, string reason)
    {
        return new SmtRuling(index, null, SmtModelCheck.NONE, SmtRunWords.NO_RESULT, reason, 0, false, "", true, null, 0);
    }

    /// <summary>A problem whose dispute was not settled.</summary>
    /// <param name="index">The problem.</param>
    /// <param name="dispute">The dispute.</param>
    /// <param name="copies">The answers it was ruled on.</param>
    /// <param name="repaired">Whether the repair round's answers are among those it was first ruled on.</param>
    /// <returns>The ruling.</returns>
    public static SmtRuling Disputed(int index, SmtDisputeRecord dispute, int copies, bool repaired)
    {
        return new SmtRuling(index, null, SmtModelCheck.NONE, SmtRunWords.DISPUTED, dispute.Outcome, copies, false, "", repaired, dispute, 0);
    }

    #endregion

    #region Properties

    /// <summary>Whether the problem has an answer.</summary>
    public bool HasAnswer => Accepted != null;

    #endregion
}
