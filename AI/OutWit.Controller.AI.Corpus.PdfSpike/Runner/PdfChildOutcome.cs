namespace OutWit.Controller.AI.Corpus.PdfSpike.Runner;

/// <summary>How a child process ended.</summary>
/// <param name="ExitCode">The exit code.</param>
/// <param name="Stdout">Its standard output.</param>
/// <param name="Stderr">Its standard error.</param>
/// <param name="Killed">"memory" or "timeout" when the parent killed it, else null.</param>
/// <param name="PeakBytes">The highest working set sampled.</param>
/// <param name="Wall">The wall time.</param>
public sealed record PdfChildOutcome(int ExitCode, string Stdout, string Stderr, string? Killed, long PeakBytes, TimeSpan Wall);
