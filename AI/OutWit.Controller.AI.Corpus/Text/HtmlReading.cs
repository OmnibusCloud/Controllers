namespace OutWit.Controller.AI.Corpus.Text;

/// <summary>How the reading of one HTML page ended.</summary>
public enum HtmlReading
{
    /// <summary>The page was parsed and its text taken (it may be empty).</summary>
    Text = 0,

    /// <summary>The page was refused before the parser saw it: its markup nests deeper or holds more tags than a page may (<c>html_refused</c>).</summary>
    Refused = 1,

    /// <summary>The parser itself went past its budget while building the page's tree: an element at the nesting limit, or more elements than a page may have (<c>html_too_complex</c>).</summary>
    TooComplex = 2
}
