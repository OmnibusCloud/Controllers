using System.Text;
using OutWit.Controller.AI.Corpus.Text;

namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>
/// Shapes the extractor's text into pages of lines: the text splits at U+000C (one per page
/// break, nothing after the last page), each page's whitespace collapses the way a WET or HTML
/// document's does (runs to one space, lines trimmed, blank lines gone), and the running headers
/// and footers leave every page. A running line is one that, with each run of digits folded to
/// one, appears on half the pages or more of a PDF of <see cref="PdfStageLimits.RUNNING_LINE_MIN_PAGES"/>
/// pages or more (a page number, "Page 3 of 12", a title or a disclaimer repeated on every
/// page): left in, it is the repetition the quality filter's duplicate rules drop a document for.
/// <para>
/// Nothing is reflowed and no hyphen is removed here: pdf_oxide already joins a paragraph's lines
/// and removes a hyphen between two lower-case parts; a hyphen it kept is the text's.
/// </para>
/// </summary>
public static class PdfTextShaper
{
    #region Constants

    /// <summary>The module's page separator.</summary>
    public const char PAGE_BREAK = '\f';

    private const char FOLDED_DIGITS = '0';

    #endregion

    #region Functions

    /// <summary>The pages of a text, collapsed, running lines removed.</summary>
    /// <param name="text">The module's text.</param>
    /// <returns>The pages (an empty page has no line) and the number of lines removed as running lines.</returns>
    public static PdfShapedText Shape(string text)
    {
        var pages = text.Split(PAGE_BREAK)
            .Select(page => HtmlTextExtractor.CollapseLines(page))
            .Select(page => page.Length == 0 ? Array.Empty<string>() : page.Split('\n'))
            .ToList();

        if (pages.Count < PdfStageLimits.RUNNING_LINE_MIN_PAGES)
            return new PdfShapedText(pages, 0);

        var running = RunningLines(pages);
        if (running.Count == 0)
            return new PdfShapedText(pages, 0);

        var removed = 0;
        var kept = new List<string[]>(pages.Count);
        foreach (var page in pages)
        {
            var lines = page.Where(line => !running.Contains(Fold(line))).ToArray();
            removed += page.Length - lines.Length;
            kept.Add(lines);
        }

        return new PdfShapedText(kept, removed);
    }

    /// <summary>A line with every run of decimal digits folded to one <c>0</c>: "Page 12 of 30" and "Page 3 of 30" are one line.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The folded line.</returns>
    public static string Fold(string line)
    {
        var builder = new StringBuilder(line.Length);
        var inDigits = false;
        foreach (var c in line)
        {
            if (char.IsDigit(c))
            {
                if (!inDigits)
                    builder.Append(FOLDED_DIGITS);
                inDigits = true;
                continue;
            }

            inDigits = false;
            builder.Append(c);
        }

        return builder.ToString();
    }

    private static HashSet<string> RunningLines(IReadOnlyList<string[]> pages)
    {
        // Pages a folded line appears on (once per page, however often it repeats there).
        var pagesOf = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            seen.Clear();
            foreach (var line in page)
            {
                var folded = Fold(line);
                if (seen.Add(folded))
                    pagesOf[folded] = pagesOf.GetValueOrDefault(folded) + 1;
            }
        }

        // Half the pages or more: twice the count reaches the page count.
        return pagesOf.Where(pair => 2L * pair.Value >= pages.Count).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
    }

    #endregion
}
