namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>
/// A fault of the node, not of the PDF: the process cannot run the sandbox at all (no module beside
/// the controller, another binding version loaded first, no native library), the wall-clock backstop
/// was reached with fuel left, or the module could not be instantiated. No document gets a verdict
/// from it: it leaves <c>Corpus.ProcessShard</c> as an exception, the node's batch fails, and
/// <c>Grid.ForEach</c> gives the node's tasks to another node and keeps this one out of the job.
/// </summary>
/// <param name="message">What happened, in fixed words.</param>
public sealed class PdfSandboxException(string message) : Exception(message);
