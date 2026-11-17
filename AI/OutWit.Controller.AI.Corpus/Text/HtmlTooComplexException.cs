namespace OutWit.Controller.AI.Corpus.Text;

/// <summary>
/// The HTML parser went past its budget on a page: thrown from the parser's own callback, so the
/// parse stops where the budget ends. The page has no text and drops as <c>html_too_complex</c>.
/// </summary>
/// <param name="reason">Which limit was reached.</param>
public sealed class HtmlTooComplexException(string reason) : Exception(reason);
