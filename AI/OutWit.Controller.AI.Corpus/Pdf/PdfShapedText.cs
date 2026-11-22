namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>The extractor's text as pages of lines, running lines removed, broken Cyrillic and Greek words rejoined.</summary>
/// <param name="Pages">The pages in document order; an empty page has no line.</param>
/// <param name="RunningLinesRemoved">The lines removed as running headers and footers, over all pages.</param>
/// <param name="HyphensJoined">The line breaks removed inside words (<see cref="PdfTextHyphens"/>), over all pages.</param>
public sealed record PdfShapedText(IReadOnlyList<string[]> Pages, int RunningLinesRemoved, int HyphensJoined = 0);
