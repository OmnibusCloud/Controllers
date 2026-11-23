namespace OutWit.Controller.AI.Smt.Live.Server;

/// <summary>How a job ended.</summary>
/// <param name="Completed">Whether it completed; false when it failed or was cancelled.</param>
/// <param name="Status">The server's word for the end: <c>Completed</c>, <c>Failed</c>, <c>Cancelled</c>.</param>
/// <param name="Error">The server's message of a failure; null without one.</param>
/// <param name="Duration">How long the tool waited for it.</param>
internal sealed record SmtLiveJobEnd(bool Completed, string Status, string? Error, TimeSpan Duration);
