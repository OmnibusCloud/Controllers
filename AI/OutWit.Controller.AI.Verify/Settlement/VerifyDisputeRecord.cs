namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>One task whose executions disagreed, and what three more came to.</summary>
/// <param name="Index">The task.</param>
/// <param name="FirstCopies">The executions of the first fan-out that had a verdict.</param>
/// <param name="Copies">The executions of the dispute round that came back with a verdict.</param>
/// <param name="Agreeing">The most of them that returned one result.</param>
/// <param name="Outcome">"resolved", or why the dispute stands (<see cref="VerifyRulings"/>).</param>
internal sealed record VerifyDisputeRecord(int Index, int FirstCopies, int Copies, int Agreeing, string Outcome)
{
    /// <summary>Whether a majority settled the dispute.</summary>
    public bool Resolved => Outcome == VerifyRulings.RESOLVED;
}
