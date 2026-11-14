namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>The extractor's text as pages of lines, running lines removed.</summary>
/// <param name="Pages">The pages in document order; an empty page has no line.</param>
/// <param name="RunningLinesRemoved">The lines removed as running headers and footers, over all pages.</param>
public sealed record PdfShapedText(IReadOnlyList<string[]> Pages, int RunningLinesRemoved);
