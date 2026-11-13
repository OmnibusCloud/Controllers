namespace OutWit.Controller.AI.Corpus.PdfSpike.Engines;

/// <summary>
/// One PDF text extractor under test: bytes in, the text of every page out, pages separated by
/// a form feed. Page failures are counted, not thrown; a document that cannot be opened throws.
/// </summary>
public interface IPdfTextEngine
{
    /// <summary>Extracts the text of a document.</summary>
    /// <param name="pdf">The document bytes.</param>
    /// <param name="maxPages">Pages read at most (0 = all).</param>
    /// <returns>The text and the page counters.</returns>
    PdfTextOutcome Extract(byte[] pdf, int maxPages);

    /// <summary>The engine's name on the command line and in the reports.</summary>
    string Name { get; }
}
