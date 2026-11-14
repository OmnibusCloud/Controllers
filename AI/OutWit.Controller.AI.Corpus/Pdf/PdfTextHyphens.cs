using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>
/// Rejoins words broken at a line end in the scripts pdf_oxide does not: its own rule removes a
/// hyphen between two lower-case Latin parts and leaves every other script's broken words as two
/// lines (in one Common Crawl WARC file, 99 of the 218 such breaks pdf_oxide left are Cyrillic). The
/// rule here is the same one for Cyrillic and Greek: a line that ends in a hyphen right after a
/// lower-case letter, and a next line that starts with a lower-case letter of the same script,
/// become one line without the hyphen. Lower case is the Unicode general category <c>Ll</c>; a
/// script is the set of its Unicode blocks as the framework defines them (<see cref="UnicodeRanges"/>).
/// Latin is not touched (pdf_oxide decided there), nor is a break between two scripts, an
/// upper-case start, a digit or a mark. A compound that the typesetter broke at its own hyphen
/// ("предметно-" / "пространственной") is joined too: the text cannot tell the two apart.
/// </summary>
public static class PdfTextHyphens
{
    #region Constants

    /// <summary>The scripts the rule applies to, each as its Unicode blocks.</summary>
    public static readonly IReadOnlyList<(string Name, IReadOnlyList<UnicodeRange> Blocks)> SCRIPTS =
    [
        ("Cyrillic", [UnicodeRanges.Cyrillic, UnicodeRanges.CyrillicSupplement, UnicodeRanges.CyrillicExtendedA, UnicodeRanges.CyrillicExtendedB, UnicodeRanges.CyrillicExtendedC]),
        ("Greek", [UnicodeRanges.GreekandCoptic, UnicodeRanges.GreekExtended])
    ];

    /// <summary>The characters taken as a hyphen at a line end: the hyphen-minus, the hyphen and the soft hyphen.</summary>
    public static readonly IReadOnlySet<char> HYPHENS = new HashSet<char> { '-', '‐', '­' };

    #endregion

    #region Functions

    /// <summary>A page's lines with every broken word of the scripts rejoined.</summary>
    /// <param name="lines">The page's lines, collapsed.</param>
    /// <param name="joined">The line breaks removed.</param>
    /// <returns>The lines; the same array when nothing was joined.</returns>
    public static string[] Join(string[] lines, out int joined)
    {
        joined = 0;
        if (lines.Length < 2)
            return lines;

        List<string>? result = null;
        for (var index = 1; index < lines.Length; index++)
        {
            var previous = result == null ? lines[index - 1] : result[^1];
            if (!Joins(previous, lines[index]))
            {
                result?.Add(lines[index]);
                continue;
            }

            result ??= [.. lines[..index]];
            result[^1] = previous[..^1] + lines[index];
            joined++;
        }

        return result == null ? lines : [.. result];
    }

    /// <summary>Whether two lines are one word broken at the line end (see the class remarks).</summary>
    /// <param name="previous">The line that may end in the hyphen.</param>
    /// <param name="next">The line after it.</param>
    /// <returns>True when they join.</returns>
    public static bool Joins(string previous, string next)
    {
        if (previous.Length < 2 || next.Length == 0 || !HYPHENS.Contains(previous[^1]))
            return false;

        if (Rune.DecodeLastFromUtf16(previous.AsSpan(0, previous.Length - 1), out var before, out _) != OperationStatus.Done
            || Rune.DecodeFromUtf16(next, out var after, out _) != OperationStatus.Done)
            return false;

        if (Rune.GetUnicodeCategory(before) != UnicodeCategory.LowercaseLetter || Rune.GetUnicodeCategory(after) != UnicodeCategory.LowercaseLetter)
            return false;

        var script = Script(before);
        return script != null && script == Script(after);
    }

    /// <summary>The script of a character among <see cref="SCRIPTS"/>, or null.</summary>
    /// <param name="rune">The character.</param>
    /// <returns>The script's name, or null for any other script.</returns>
    public static string? Script(Rune rune)
    {
        foreach (var (name, blocks) in SCRIPTS)
        {
            foreach (var block in blocks)
            {
                if (rune.Value >= block.FirstCodePoint && rune.Value < block.FirstCodePoint + block.Length)
                    return name;
            }
        }

        return null;
    }

    #endregion
}
