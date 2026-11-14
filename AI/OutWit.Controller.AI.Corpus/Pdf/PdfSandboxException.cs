namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>
/// A fault of the host that ran a PDF, not of the PDF: the wall-clock backstop was reached with
/// fuel left, or the module could not be instantiated. The shard fails (and runs elsewhere); no
/// document gets a verdict from it.
/// </summary>
/// <param name="message">What happened, in fixed words.</param>
public sealed class PdfSandboxException(string message) : Exception(message);
