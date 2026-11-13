using System.Text;
using PdfLibrary.Structure;

namespace OutWit.Controller.AI.Corpus.PdfSpike.Engines;

/// <summary>
/// PdfLibrary (Lxman.PdfLibrary, MIT): the document loaded from memory, each page's text by the
/// library's own <c>PdfPage.ExtractText()</c>. The library has no options for limits, no filter
/// hook and no cancellation, so there is no budgeted variant.
/// </summary>
public sealed class PdfTextEnginePdfLibrary : IPdfTextEngine
{
    #region Functions

    /// <inheritdoc />
    public PdfTextOutcome Extract(byte[] pdf, int maxPages)
    {
        using var stream = new MemoryStream(pdf, writable: false);
        using var document = PdfDocument.Load(stream);

        // One walk of the page tree: GetPage(i) walks it again for every page (quadratic in pages).
        var all = document.GetPages();
        var pages = all.Count;
        var read = maxPages > 0 ? Math.Min(pages, maxPages) : pages;
        var failed = 0;
        string? firstError = null;
        var text = new StringBuilder();

        for (var index = 0; index < read; index++)
        {
            if (index > 0)
                text.Append('\f');

            try
            {
                text.Append(all[index].ExtractText());
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                failed++;
                firstError ??= exception.GetType().Name;
            }
        }

        return new PdfTextOutcome(text.ToString(), pages, read, failed, firstError);
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public string Name => "pdflibrary";

    #endregion
}
