using System.Globalization;
using System.Text.RegularExpressions;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Docs;

/// <summary>
/// Reads what the tests of the documents need of a Markdown file of the repository: a table by the
/// heading above it, the code spans of a cell, a fenced block by the marker above it, the links,
/// and the anchors its headings give. Only the shapes these documents are written in: a table is
/// a block of lines that start and end with a bar; a bar inside a cell is written <c>\|</c>.
/// </summary>
internal static partial class SmtMarkdown
{
    #region Constants

    private const string FENCE = "```";

    #endregion

    #region Functions

    /// <summary>A file of the repository, by its path from the repository's root.</summary>
    /// <param name="path">The path, with forward slashes.</param>
    /// <returns>Its text, with line feeds.</returns>
    public static string Read(string path)
    {
        return File.ReadAllText(PathOf(path)).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>The full path of a file of the repository.</summary>
    /// <param name="path">The path from the repository's root, with forward slashes.</param>
    /// <returns>The full path.</returns>
    public static string PathOf(string path)
    {
        return Path.Combine([SmtRigModules.SolutionRoot()!, .. path.Split('/')]);
    }

    /// <summary>The first table after a heading, before the next heading.</summary>
    /// <param name="text">The document.</param>
    /// <param name="heading">The heading's line, as written (<c>## The profile</c>).</param>
    /// <returns>The table.</returns>
    /// <exception cref="InvalidOperationException">No such heading, or no table under it.</exception>
    public static SmtMarkdownTable Table(string text, string heading)
    {
        var lines = text.Split('\n');
        var start = Array.IndexOf(lines, heading);
        if (start < 0)
            throw new InvalidOperationException($"the document has no heading '{heading}'");

        var table = new List<IReadOnlyList<string>>();
        for (var index = start + 1; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith('#'))
                break;

            if (line.StartsWith('|') && line.EndsWith('|'))
                table.Add(Cells(line));
            else if (table.Count > 0)
                break;
        }

        if (table.Count < 2)
            throw new InvalidOperationException($"no table under '{heading}'");

        return new SmtMarkdownTable(table[0], table.Skip(2).ToList());
    }

    /// <summary>The texts of the code spans of a cell, in their order.</summary>
    /// <param name="cell">The cell.</param>
    /// <returns>The texts, without the backticks.</returns>
    public static List<string> Codes(string cell)
    {
        return [.. CodeSpan().Matches(cell).Select(match => match.Groups[1].Value)];
    }

    /// <summary>The fenced block that follows a marker line (<c>&lt;!-- example: name --&gt;</c>).</summary>
    /// <param name="text">The document.</param>
    /// <param name="name">The example's name.</param>
    /// <returns>The block's content, without its fences; empty for an empty block.</returns>
    /// <exception cref="InvalidOperationException">No such marker, or no block right after it.</exception>
    public static string Block(string text, string name)
    {
        var (start, end) = BlockLines(text.Split('\n'), name);
        return string.Join("\n", text.Split('\n')[(start + 1)..end]);
    }

    /// <summary>A document with the content of the block after a marker replaced.</summary>
    /// <param name="text">The document.</param>
    /// <param name="name">The example's name.</param>
    /// <param name="content">The new content, without fences.</param>
    /// <returns>The document.</returns>
    public static string Replace(string text, string name, string content)
    {
        var lines = text.Split('\n');
        var (start, end) = BlockLines(lines, name);
        var replaced = lines[..(start + 1)].Concat(content.Length == 0 ? [] : content.Split('\n')).Concat(lines[end..]);
        return string.Join("\n", replaced);
    }

    /// <summary>The targets of the inline links of a document, in their order.</summary>
    /// <param name="text">The document.</param>
    /// <returns>The targets as written.</returns>
    public static List<string> Links(string text)
    {
        return [.. Link().Matches(WithoutCode(text)).Select(match => match.Groups[1].Value)];
    }

    /// <summary>The anchors the headings of a document give on the repository's site.</summary>
    /// <param name="text">The document.</param>
    /// <returns>The anchors, without the <c>#</c>.</returns>
    public static HashSet<string> Anchors(string text)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n').Where(line => line.StartsWith('#')))
        {
            var title = line.TrimStart('#').Trim().ToLowerInvariant();
            var anchor = new string([.. title.Where(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_')]).Replace(' ', '-');
            anchors.Add(anchor);
        }

        return anchors;
    }

    /// <summary>A number as the documents write it: with a comma between thousands.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The text.</returns>
    public static string Number(long number)
    {
        return number.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>A number as a program writes it: no separators.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The text.</returns>
    public static string Plain(double number)
    {
        return number.ToString(CultureInfo.InvariantCulture);
    }

    #endregion

    #region Tools

    private static IReadOnlyList<string> Cells(string line)
    {
        var inner = line[1..^1];
        return [.. Bar().Split(inner).Select(cell => cell.Replace("\\|", "|", StringComparison.Ordinal).Trim())];
    }

    private static (int Start, int End) BlockLines(string[] lines, string name)
    {
        var marker = Array.IndexOf(lines, $"<!-- example: {name} -->");
        if (marker < 0 || marker + 1 >= lines.Length || !lines[marker + 1].StartsWith(FENCE, StringComparison.Ordinal))
            throw new InvalidOperationException($"no fenced block right after the marker of the example '{name}'");

        var end = Array.FindIndex(lines, marker + 2, line => line == FENCE);
        if (end < 0)
            throw new InvalidOperationException($"the block of the example '{name}' is not closed");

        return (marker + 1, end);
    }

    /// <summary>The text without its fenced blocks and code spans, where a link is no link.</summary>
    private static string WithoutCode(string text)
    {
        var lines = new List<string>();
        var fenced = false;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith(FENCE, StringComparison.Ordinal))
                fenced = !fenced;
            else if (!fenced)
                lines.Add(line);
        }

        return CodeSpan().Replace(string.Join("\n", lines), "");
    }

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex Bar();

    [GeneratedRegex("`([^`]*)`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    #endregion
}
