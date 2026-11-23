using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>One problem that went to three more machines, and what they came to.</summary>
/// <param name="Index">The problem.</param>
/// <param name="Cause">Why: the copies' digests differed, the evaluator called the model failed, or the package had more answers than were asked for (<see cref="SmtRunWords.DISPUTE_CAUSES"/>).</param>
/// <param name="FirstCopies">The answers of the round the problem was first ruled on.</param>
/// <param name="Copies">The answers of the dispute round that came back.</param>
/// <param name="Agreeing">The most of them that gave one digest.</param>
/// <param name="Outcome">"resolved", or why the dispute stands (<see cref="SmtRunWords.DISPUTE_OUTCOMES"/>).</param>
internal sealed record SmtDisputeRecord(int Index, string Cause, int FirstCopies, int Copies, int Agreeing, string Outcome)
{
    /// <summary>Whether a majority settled the dispute.</summary>
    public bool Resolved => Outcome == SmtRunWords.RESOLVED;
}
