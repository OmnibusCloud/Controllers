namespace OutWit.Controller.AI.Corpus.PdfSpike.Engines;

/// <summary>What an engine made of one document.</summary>
/// <param name="Text">The text, pages separated by a form feed.</param>
/// <param name="Pages">The page count the document declares (or the engine found).</param>
/// <param name="PagesRead">Pages the engine was asked to read.</param>
/// <param name="PagesFailed">Pages whose extraction threw.</param>
/// <param name="FirstPageError">The exception type of the first page that threw, or null.</param>
public sealed record PdfTextOutcome(string Text, int Pages, int PagesRead, int PagesFailed, string? FirstPageError);
