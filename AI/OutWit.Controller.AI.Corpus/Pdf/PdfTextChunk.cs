using System.Globalization;

namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>One page-range document of a PDF.</summary>
/// <param name="FirstPage">The first page of the range, 1-based.</param>
/// <param name="LastPage">The last page of the range, inclusive.</param>
/// <param name="Text">The range's lines, joined by LF.</param>
public sealed record PdfTextChunk(int FirstPage, int LastPage, string Text)
{
    #region Functions

    /// <summary>The document's id: the PDF's URL (its own fragment, if it had one, replaced) and the range.</summary>
    /// <param name="url">The PDF's URL.</param>
    /// <returns><c>url#page=a-b</c>.</returns>
    public string Id(string url)
    {
        var hash = url.IndexOf('#');
        var bare = hash >= 0 ? url[..hash] : url;
        return string.Create(CultureInfo.InvariantCulture, $"{bare}#page={FirstPage}-{LastPage}");
    }

    #endregion
}
