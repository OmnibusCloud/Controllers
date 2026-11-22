using System.Text;
using OutWit.Controller.AI.Corpus.Quality;

namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>
/// Cuts a PDF's shaped pages into page-range documents of at most
/// <see cref="PdfStageLimits.DOCUMENT_WORDS"/> words. Pages are taken in order and put together
/// while the document still fits; the page that would not fit starts the next document, so every
/// document is a whole range of pages and its id says which (<c>url#page=a-b</c>, 1-based,
/// inclusive; empty pages between two pages with text belong to the range, empty pages around it
/// do not). A page longer than the cap is a document of its own, cut after the last whole line
/// that fits (a line that alone is longer is cut after its last word that fits) and the rest of
/// the page dropped: a page is the smallest unit the id can name, a page five times a dense page
/// of prose is a table, an index or a text layer gone wrong, and keeping its first lines bounds
/// every document without two documents of one id.
/// <para>
/// A word is what the quality filter counts in a mixed text: a run of characters between spaces
/// with a letter or a digit in it, and in a script written without spaces each of its letters.
/// </para>
/// </summary>
public static class PdfTextChunker
{
    #region Functions

    /// <summary>The documents of a PDF.</summary>
    /// <param name="pages">The shaped pages (<see cref="PdfTextShaper.Shape"/>).</param>
    /// <param name="maxWords">The most words of one document.</param>
    /// <returns>The documents in page order, and the pages cut at the cap.</returns>
    public static (IReadOnlyList<PdfTextChunk> Chunks, int PagesCut) Chunk(IReadOnlyList<string[]> pages, int maxWords = PdfStageLimits.DOCUMENT_WORDS)
    {
        maxWords = Math.Max(1, maxWords);
        var chunks = new List<PdfTextChunk>();
        var lines = new List<string>();
        var first = 0;
        var last = 0;
        var words = 0;
        var cut = 0;

        void Flush()
        {
            if (lines.Count > 0)
                chunks.Add(new PdfTextChunk(first, last, string.Join('\n', lines)));

            lines.Clear();
            words = 0;
        }

        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            if (page.Length == 0)
                continue;

            var number = index + 1;
            var pageWords = page.Sum(Words);

            if (pageWords > maxWords)
            {
                Flush();
                chunks.Add(new PdfTextChunk(number, number, Cut(page, maxWords)));
                cut++;
                continue;
            }

            if (lines.Count > 0 && words + pageWords > maxWords)
                Flush();

            if (lines.Count == 0)
                first = number;

            lines.AddRange(page);
            last = number;
            words += pageWords;
        }

        Flush();
        return (chunks, cut);
    }

    /// <summary>The words of a line (see the class remarks).</summary>
    /// <param name="line">The line.</param>
    /// <returns>The count.</returns>
    public static int Words(string line)
    {
        return WordStarts(line, int.MaxValue, out _);
    }

    /// <summary>The longest beginning of a line with at most so many words, cut before a word, trailing spaces trimmed.</summary>
    /// <param name="line">The line.</param>
    /// <param name="maxWords">The most words.</param>
    /// <returns>The beginning (the whole line when it fits).</returns>
    public static string Prefix(string line, int maxWords)
    {
        WordStarts(line, maxWords, out var end);
        return end < 0 ? line : line[..end].TrimEnd();
    }

    private static string Cut(string[] page, int maxWords)
    {
        var taken = new List<string>();
        var words = 0;
        foreach (var line in page)
        {
            var lineWords = Words(line);
            if (words + lineWords <= maxWords)
            {
                taken.Add(line);
                words += lineWords;
                continue;
            }

            // Not one whole line fits: the first is cut between two words.
            if (taken.Count == 0)
                taken.Add(Prefix(line, maxWords));

            break;
        }

        return string.Join('\n', taken);
    }

    /// <summary>
    /// Counts the words of a line, stopping at the word that would be one past the limit.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="limit">The most words to count.</param>
    /// <param name="end">The index the word past the limit starts at, or -1 when the line has no more words than the limit.</param>
    /// <returns>The words counted (at most the limit).</returns>
    private static int WordStarts(string line, int limit, out int end)
    {
        var count = 0;
        var inRun = false;
        var runCounted = false;
        var runStart = 0;
        end = -1;

        for (var index = 0; index < line.Length;)
        {
            // An unpaired surrogate reads as U+FFFD (a symbol), never as an exception.
            Rune.DecodeFromUtf16(line.AsSpan(index), out var rune, out var width);

            if (Rune.IsWhiteSpace(rune))
            {
                inRun = false;
            }
            else if (Rune.IsLetter(rune) && QualityFilter.IsUnspacedScript(rune.Value))
            {
                // Each letter of a script without spaces is a word of its own.
                inRun = false;
                if (count == limit)
                {
                    end = index;
                    return count;
                }

                count++;
            }
            else
            {
                if (!inRun)
                {
                    inRun = true;
                    runCounted = false;
                    runStart = index;
                }

                // A run between spaces is a word once it holds a letter or a digit.
                if (!runCounted && Rune.IsLetterOrDigit(rune))
                {
                    if (count == limit)
                    {
                        end = runStart;
                        return count;
                    }

                    count++;
                    runCounted = true;
                }
            }

            index += width;
        }

        return count;
    }

    #endregion
}
