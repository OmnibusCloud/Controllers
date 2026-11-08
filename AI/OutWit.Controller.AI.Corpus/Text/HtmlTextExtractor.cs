using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace OutWit.Controller.AI.Corpus.Text;

/// <summary>
/// HTML to text, deterministically: AngleSharp parses the document (a managed parser, the
/// same tree on every platform), a scoped rule set drops what is never corpus text (scripts,
/// styles, navigation, forms, hidden and templated content), block elements become line
/// breaks, inline text runs are joined with single spaces, and the result is lines of
/// collapsed whitespace with the title first — the shape Common Crawl's own WET conversion
/// has, so the two can be compared.
/// </summary>
public static class HtmlTextExtractor
{
    #region Constants

    /// <summary>Documents larger than this are cut before parsing: a page, not an archive.</summary>
    public const int MAX_HTML_CHARS = 4 * 1024 * 1024;

    /// <summary>
    /// Deepest element nesting accepted (browsers stop at 512). The HTML parsing algorithm
    /// walks its open-element stack per tag, so a page nested tens of thousands deep costs
    /// seconds per kilobyte; such a page is a drop, not a document.
    /// </summary>
    public const int MAX_NESTING = 512;

    /// <summary>Most tags a page may carry before it counts as hostile rather than large.</summary>
    public const int MAX_TAGS = 60_000;

    private static readonly HashSet<string> VOID_TAGS = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"
    };

    private static readonly HashSet<string> DROPPED = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "template", "svg", "canvas", "iframe", "object", "embed",
        "nav", "header", "footer", "aside", "form", "button", "select", "option", "textarea", "input",
        "head", "link", "meta", "base", "title"
    };

    private static readonly HashSet<string> BLOCKS = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "blockquote", "br", "caption", "dd", "details", "div", "dl", "dt", "fieldset",
        "figcaption", "figure", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "legend", "li", "main", "ol", "p",
        "pre", "section", "summary", "table", "tbody", "td", "tfoot", "th", "thead", "tr", "ul", "body", "html"
    };

    private static readonly HtmlParser s_parser = new(new HtmlParserOptions { IsScripting = false, IsNotConsumingCharacterReferences = false });

    #endregion

    #region Functions

    /// <summary>
    /// Extracts the text of an HTML document, or refuses a hostile one.
    /// </summary>
    /// <param name="html">The document.</param>
    /// <param name="text">Lines of text separated by '\n', the title first when present; "" for a document without text or a refused one.</param>
    /// <param name="refusal">Why the document was refused (nesting or tag count past the caps), or null.</param>
    /// <returns>True when the document was extracted, false when it was refused.</returns>
    public static bool TryExtract(string html, out string text, out string? refusal)
    {
        refusal = Inspect(html);
        if (refusal != null)
        {
            text = "";
            return false;
        }

        text = Extract(html);
        return true;
    }

    /// <summary>
    /// Extracts the text of an HTML document. Callers that may meet hostile pages use
    /// <see cref="TryExtract"/>, which refuses them before the parser pays for them.
    /// </summary>
    /// <param name="html">The document.</param>
    /// <returns>Lines of text separated by '\n', the title first when present; "" for a document without text.</returns>
    public static string Extract(string html)
    {
        if (html.Length > MAX_HTML_CHARS)
            html = html[..MAX_HTML_CHARS];

        using var document = s_parser.ParseDocument(html);

        var lines = new List<string>();
        var title = Collapse(document.Title ?? "");
        if (title.Length > 0)
            lines.Add(title);

        var current = new StringBuilder();
        if (document.Body != null)
            Walk(document.Body, current, lines);
        Flush(current, lines);

        return string.Join('\n', lines);
    }

    /// <summary>
    /// A linear pre-scan of the markup: counts tags and tracks nesting from open and close
    /// tags (void elements and self-closing tags do not nest), without parsing.
    /// </summary>
    /// <param name="html">The document.</param>
    /// <returns>The refusal reason, or null when the page is within the caps.</returns>
    public static string? Inspect(string html)
    {
        var length = Math.Min(html.Length, MAX_HTML_CHARS);
        var depth = 0;
        var maxDepth = 0;
        var tags = 0;
        var i = 0;
        while (i < length)
        {
            var open = html.IndexOf('<', i);
            if (open < 0 || open >= length)
                break;

            var close = html.IndexOf('>', open + 1);
            if (close < 0 || close >= length)
                break;

            i = close + 1;
            if (open + 1 >= length)
                break;

            var c = html[open + 1];
            if (c == '!' || c == '?')
            {
                // Comments and declarations: skip a comment to its end so its contents never count.
                if (string.CompareOrdinal(html, open, "<!--", 0, 4) == 0)
                {
                    var end = html.IndexOf("-->", open + 4, StringComparison.Ordinal);
                    i = end < 0 ? length : end + 3;
                }

                continue;
            }

            tags++;
            if (tags > MAX_TAGS)
                return $"more than {MAX_TAGS} tags";

            if (c == '/')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (!char.IsLetter(c))
                continue;

            var nameEnd = open + 1;
            while (nameEnd < close && (char.IsLetterOrDigit(html[nameEnd]) || html[nameEnd] == '-'))
                nameEnd++;
            var name = html.AsSpan(open + 1, nameEnd - open - 1);

            if (html[close - 1] == '/' || VOID_TAGS.Contains(name.ToString()))
                continue;

            if (name.Equals("script", StringComparison.OrdinalIgnoreCase) || name.Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                // Raw text elements: their content is not markup; jump to the closing tag.
                var end = html.IndexOf("</" + name.ToString(), close + 1, StringComparison.OrdinalIgnoreCase);
                i = end < 0 ? length : end;
                continue;
            }

            depth++;
            if (depth > maxDepth)
            {
                maxDepth = depth;
                if (maxDepth > MAX_NESTING)
                    return $"elements nested deeper than {MAX_NESTING}";
            }
        }

        return null;
    }

    private static void Walk(INode node, StringBuilder current, List<string> lines)
    {
        // Iterative, not recursive: a hostile page nests tens of thousands of elements deep.
        var stack = new Stack<(INode Node, bool Exit)>();
        stack.Push((node, false));

        while (stack.Count > 0)
        {
            var (item, exit) = stack.Pop();

            if (item is IElement element)
            {
                var tag = element.LocalName;
                if (exit)
                {
                    if (BLOCKS.Contains(tag))
                        Flush(current, lines);
                    continue;
                }

                if (DROPPED.Contains(tag) || IsHidden(element))
                    continue;

                if (BLOCKS.Contains(tag))
                    Flush(current, lines);

                stack.Push((item, true));
                for (var child = item.LastChild; child != null; child = child.PreviousSibling)
                    stack.Push((child, false));
                continue;
            }

            if (item is IText text)
            {
                var run = text.Data;
                if (run.Length == 0)
                    continue;

                if (current.Length > 0 && !char.IsWhiteSpace(current[^1]))
                    current.Append(' ');
                current.Append(run);
            }
        }
    }

    private static bool IsHidden(IElement element)
    {
        if (element.HasAttribute("hidden"))
            return true;

        if (element is IHtmlElement { } html && html.GetAttribute("aria-hidden") is "true")
            return true;

        var style = element.GetAttribute("style");
        return style != null && style.Contains("display:none", StringComparison.OrdinalIgnoreCase) || style != null && style.Contains("display: none", StringComparison.OrdinalIgnoreCase);
    }

    private static void Flush(StringBuilder current, List<string> lines)
    {
        if (current.Length == 0)
            return;

        var line = Collapse(current.ToString());
        current.Clear();
        if (line.Length > 0)
            lines.Add(line);
    }

    /// <summary>Collapses every run of whitespace (including no-break spaces) to one space and trims.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The collapsed text.</returns>
    public static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || c == ' ' || c == '​')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    #endregion
}
