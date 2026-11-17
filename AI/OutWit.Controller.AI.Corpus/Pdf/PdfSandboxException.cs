namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>
/// A fault of the node, not of the PDF: the process cannot run the sandbox at all (no module beside
/// the controller, another binding version loaded first, no native library), the wall-clock backstop
/// was reached with fuel left, or the module could not be instantiated. No document gets a verdict
/// from it. A node that has no sandbox knows so before it starts and refuses its batch
/// (<see cref="Shards.ShardBatchGuard"/>); a fault met while a shard runs returns that shard
/// unanswered, as a node fault with this exception's code, and the script sends it out once more.
/// </summary>
/// <param name="code">The fault's code: <see cref="Trust.CorpusResultCodes.FAULT_PDF_BACKSTOP"/> or <see cref="Trust.CorpusResultCodes.FAULT_PDF_SANDBOX"/>.</param>
/// <param name="message">What happened, in fixed words: the node's own log reads it, nothing else does.</param>
public sealed class PdfSandboxException(string code, string message) : Exception(message)
{
    #region Properties

    /// <summary>The fault's code.</summary>
    public string Code { get; } = code;

    #endregion
}
