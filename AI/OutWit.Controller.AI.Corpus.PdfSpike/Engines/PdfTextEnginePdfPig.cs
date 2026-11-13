using System.Text;
using OutWit.Controller.AI.Corpus.PdfSpike.Guards;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace OutWit.Controller.AI.Corpus.PdfSpike.Engines;

/// <summary>
/// PdfPig (UglyToad.PdfPig, Apache-2.0): lenient parsing, missing fonts skipped, the page text
/// assembled in content order by the library's own <see cref="ContentOrderTextExtractor"/>
/// (spaces and line breaks from the letters' geometry). With a budget, every stream decode goes
/// through <see cref="PdfPigFilterProviderBudget"/>, which refuses a stream or a document that
/// inflates past its caps and a document past its deadline.
/// </summary>
public sealed class PdfTextEnginePdfPig : IPdfTextEngine
{
    #region Fields

    private readonly PdfDecodeBudget? m_budget;

    #endregion

    #region Constructors

    /// <summary>Creates the engine.</summary>
    /// <param name="budget">The decode budget, or null for the library as it ships.</param>
    public PdfTextEnginePdfPig(PdfDecodeBudget? budget)
    {
        m_budget = budget;
    }

    #endregion

    #region Functions

    /// <inheritdoc />
    public PdfTextOutcome Extract(byte[] pdf, int maxPages)
    {
        var options = new ParsingOptions
        {
            UseLenientParsing = true,
            SkipMissingFonts = true,
            FilterProvider = m_budget == null ? null : new PdfPigFilterProviderBudget(m_budget),
        };

        using var document = PdfDocument.Open(pdf, options);

        var pages = document.NumberOfPages;
        var read = maxPages > 0 ? Math.Min(pages, maxPages) : pages;
        var failed = 0;
        string? firstError = null;
        var text = new StringBuilder();

        for (var number = 1; number <= read; number++)
        {
            if (number > 1)
                text.Append('\f');

            try
            {
                var page = document.GetPage(number);
                text.Append(ContentOrderTextExtractor.GetText(page));
            }
            catch (PdfBudgetExceededException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                failed++;
                firstError ??= exception.GetType().Name;
            }

            m_budget?.CheckDeadline();
        }

        return new PdfTextOutcome(text.ToString(), pages, read, failed, firstError);
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public string Name => m_budget == null ? "pdfpig" : "pdfpig-budget";

    #endregion
}
