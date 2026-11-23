namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// What a session with a scripted solver came to (<see cref="SmtSessionScript"/>): the outcome,
/// and everything the session sent, one entry each time the solver read.
/// </summary>
/// <param name="Outcome">The session's outcome.</param>
/// <param name="Sent">What the session gave the solver at each of its reads, as text; the end of input is not an entry.</param>
/// <param name="Ended">Whether the session ended the input itself (it was asked, and gave nothing more).</param>
internal sealed record SmtScripted(SmtSolved Outcome, IReadOnlyList<string> Sent, bool Ended);
