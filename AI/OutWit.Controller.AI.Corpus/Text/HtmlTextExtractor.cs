using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Text;
using OutWit.Controller.AI.Corpus.Text.Casing;

namespace OutWit.Controller.AI.Corpus.Text;

/// <summary>
/// HTML to text, deterministically: AngleSharp parses the document (a managed parser, the
/// same tree on every platform), a scoped rule set drops what is never corpus text (scripts,
/// styles, navigation, forms, hidden and templated content), block elements become line
/// breaks, inline text runs are joined with single spaces, and the result is lines of
/// collapsed whitespace with the title first — the shape Common Crawl's own WET conversion
/// has, so the two can be compared.
/// <para>
/// A page is hostile input, and the parser's work on it is bounded twice. A linear pre-scan
/// (<see cref="Inspect"/>) refuses a page whose markup, read the way a tokenizer reads it,
/// nests deeper or holds more tags than a page may, before the parser pays for it
/// (<c>html_refused</c>). The pre-scan is a reading of its own and can be argued with, so the
/// budget itself sits in the parser: its tree is held to the same nesting limit
/// (<see cref="MAX_NESTING"/>), and the parse stops the moment an element lands at that limit or
/// the tree holds more elements than a page may have (<see cref="MAX_ELEMENTS"/>, the copies the
/// parser makes on its own included: <see cref="HtmlTreeBudget"/>) — <c>html_too_complex</c>.
/// Both are functions of the page and the parser version, the same on every node.
/// </para>
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

    /// <summary>
    /// Most elements the parser's tree may hold for one page: the tags a page may carry and as
    /// many again that no tag names (the html, head, body and table parts the parser supplies,
    /// the formatting elements it opens again in every block that follows an unclosed one).
    /// </summary>
    public const int MAX_ELEMENTS = 2 * MAX_TAGS;

    private static readonly HashSet<string> VOID_TAGS = new(StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"
    };

    /// <summary>Elements whose content a tokenizer reads as text up to their end tag: no tag inside counts.</summary>
    private static readonly HashSet<string> TEXT_TAGS = new(StringComparer.Ordinal)
    {
        "script", "style", "xmp", "iframe", "noembed", "noframes", "textarea", "title"
    };

    private const string PLAINTEXT = "plaintext";

    private static readonly HashSet<string> DROPPED = new(StringComparer.Ordinal)
    {
        "script", "style", "noscript", "template", "svg", "canvas", "iframe", "object", "embed",
        "nav", "header", "footer", "aside", "form", "button", "select", "option", "textarea", "input",
        "head", "link", "meta", "base", "title"
    };

    private static readonly HashSet<string> BLOCKS = new(StringComparer.Ordinal)
    {
        "address", "article", "blockquote", "br", "caption", "dd", "details", "div", "dl", "dt", "fieldset",
        "figcaption", "figure", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "legend", "li", "main", "ol", "p",
        "pre", "section", "summary", "table", "tbody", "td", "tfoot", "th", "thead", "tr", "ul", "body", "html"
    };

    #endregion

    #region Fields

    /// <summary>One parser per thread: a node runs several shards at once and the parser's thread safety is not a documented contract.</summary>
    [ThreadStatic]
    private static HtmlParser? m_parser;

    /// <summary>The budget of the page this thread is parsing: the parser's callback counts into it.</summary>
    [ThreadStatic]
    private static HtmlTreeBudget? m_budget;

    #endregion

    #region Functions

    /// <summary>
    /// Reads an HTML document: its text, or the reason it has none.
    /// </summary>
    /// <param name="html">The document.</param>
    /// <param name="text">Lines of text separated by '\n', the title first when present; "" for a document without text or one that was not read.</param>
    /// <returns>Whether the page was read, refused by the pre-scan, or found past the parser's budget.</returns>
    public static HtmlReading Read(string html, out string text)
    {
        text = "";
        if (Inspect(html) != null)
            return HtmlReading.Refused;

        try
        {
            text = Extract(html);
            return HtmlReading.Text;
        }
        catch (HtmlTooComplexException)
        {
            return HtmlReading.TooComplex;
        }
    }

    /// <summary>
    /// Extracts the text of an HTML document, or refuses a hostile one.
    /// </summary>
    /// <param name="html">The document.</param>
    /// <param name="text">Lines of text separated by '\n', the title first when present; "" for a document without text or a refused one.</param>
    /// <param name="refusal">Why the document was not read (the pre-scan's caps, or the parser's budget), or null.</param>
    /// <returns>True when the document was extracted, false when it was not.</returns>
    public static bool TryExtract(string html, out string text, out string? refusal)
    {
        text = "";
        refusal = Inspect(html);
        if (refusal != null)
            return false;

        try
        {
            text = Extract(html);
            return true;
        }
        catch (HtmlTooComplexException exception)
        {
            refusal = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Extracts the text of an HTML document. Callers that may meet hostile pages use
    /// <see cref="Read"/>, which refuses them before the parser pays for them.
    /// </summary>
    /// <param name="html">The document.</param>
    /// <returns>Lines of text separated by '\n', the title first when present; "" for a document without text.</returns>
    /// <exception cref="HtmlTooComplexException">The parser went past its budget on the page.</exception>
    public static string Extract(string html)
    {
        if (html.Length > MAX_HTML_CHARS)
            html = html[..MAX_HTML_CHARS];

        var budget = m_budget ??= new HtmlTreeBudget(MAX_NESTING, MAX_ELEMENTS);
        budget.Reset();
        try
        {
            using var document = Parser.ParseDocument(html);

            // The last element the parser made was never followed by another.
            budget.Finish();

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
        finally
        {
            budget.Reset();
        }
    }

    /// <summary>
    /// A linear pre-scan of the markup, read the way an HTML tokenizer reads it: counts tags and
    /// tracks nesting from open and close tags (void elements and self-closing tags do not nest),
    /// without parsing. A comment ends where a tokenizer ends it (<c>&lt;!--&gt;</c> and
    /// <c>&lt;!---&gt;</c> are empty comments, <c>--!&gt;</c> closes one); the content of an
    /// element a tokenizer reads as text (script, style, xmp, iframe, noembed, noframes,
    /// textarea, title) is skipped up to that element's own end tag and no further; after
    /// <c>&lt;plaintext&gt;</c> nothing is markup. Names are compared as ASCII.
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
            if (open < 0 || open + 1 >= length)
                break;

            var c = html[open + 1];
            if (c == '!' || c == '?')
            {
                i = SkipDeclaration(html, open, length);
                continue;
            }

            var close = html.IndexOf('>', open + 1);
            if (close < 0 || close >= length)
                break;

            i = close + 1;

            tags++;
            if (tags > MAX_TAGS)
                return $"more than {MAX_TAGS} tags";

            if (c == '/')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (!char.IsAsciiLetter(c))
                continue;

            var nameEnd = open + 1;
            while (nameEnd < close && (char.IsAsciiLetterOrDigit(html[nameEnd]) || html[nameEnd] == '-'))
                nameEnd++;
            var name = CorpusCasing.ToLowerAscii(html.Substring(open + 1, nameEnd - open - 1));

            if (html[close - 1] == '/' || VOID_TAGS.Contains(name))
                continue;

            if (name == PLAINTEXT)
                break;

            if (TEXT_TAGS.Contains(name))
            {
                // Text up to the element's own end tag; without one, text to the end.
                var end = IndexOfEndTag(html, name, close + 1, length);
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

    /// <summary>Where the markup goes on after a declaration or a comment that opens at <paramref name="open"/> with <c>&lt;!</c> or <c>&lt;?</c>.</summary>
    private static int SkipDeclaration(string html, int open, int length)
    {
        if (string.CompareOrdinal(html, open, "<!--", 0, 4) == 0)
        {
            var start = open + 4;

            // An empty comment: a tokenizer ends "<!-->" and "<!--->" at once.
            if (start < length && html[start] == '>')
                return start + 1;
            if (start + 1 < length && html[start] == '-' && html[start + 1] == '>')
                return start + 2;

            // The first "-->" or "--!>", found in one pass over the comment: two searches, each to
            // the end of the page when its own ending never comes, would make a page of comments
            // cost the square of its length.
            for (var at = start; at < length; at++)
            {
                at = html.IndexOf("--", at, length - at, StringComparison.Ordinal);
                if (at < 0 || at + 2 >= length)
                    return length;

                if (html[at + 2] == '>')
                    return at + 3;

                if (html[at + 2] == '!' && at + 3 < length && html[at + 3] == '>')
                    return at + 4;
            }

            return length;
        }

        // A doctype, a processing instruction, a CDATA marker: it ends at the next '>'.
        var close = html.IndexOf('>', open + 1);
        return close < 0 ? length : close + 1;
    }

    /// <summary>The first <c>&lt;/name</c> at or after a position that a tokenizer takes for the end tag: followed by white space, '/' or '>'.</summary>
    private static int IndexOfEndTag(string html, string name, int start, int length)
    {
        var span = html.AsSpan(0, length);
        for (var index = start; index + 2 + name.Length <= length; index++)
        {
            index = html.IndexOf("</", index, length - index, StringComparison.Ordinal);
            if (index < 0 || index + 2 + name.Length > length)
                return -1;

            if (!CorpusCasing.EqualsIgnoreAsciiCase(span.Slice(index + 2, name.Length), name))
                continue;

            var after = index + 2 + name.Length;
            if (after >= length || html[after] is '>' or '/' or ' ' or '\t' or '\n' or '\r' or '\f')
                return index;
        }

        return -1;
    }

    private static HtmlParser CreateParser()
    {
        return new HtmlParser(new HtmlParserOptions
        {
            IsScripting = false,
            IsNotConsumingCharacterReferences = false,
            // The parser's own nesting limit, stated: its tree never goes deeper, whatever the
            // library's default becomes.
            MaximumDepth = MAX_NESTING,
            OnCreated = Created
        });
    }

    /// <summary>
    /// The parser's callback for every element it makes: the page's budget judges the one made
    /// before (in the tree by now) on the parser's own tree, and stops the parse past it.
    /// </summary>
    private static void Created(IElement element, TextPosition position)
    {
        m_budget?.Created(element);
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
                var tag = CorpusCasing.ToLowerAscii(element.LocalName);
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

                // By position, last child first: asking a node for its previous sibling is a
                // search of its parent's children, and a page may put all its nodes under one.
                stack.Push((item, true));
                var children = item.ChildNodes;
                for (var index = children.Length - 1; index >= 0; index--)
                    stack.Push((children[index], false));
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
        return style != null && (CorpusCasing.ContainsIgnoreAsciiCase(style, "display:none") || CorpusCasing.ContainsIgnoreAsciiCase(style, "display: none"));
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

    /// <summary>Collapses whitespace within each line and drops the empty lines, keeping the line structure (a WET document is one block per line).</summary>
    /// <param name="text">The text.</param>
    /// <returns>The non-empty lines, collapsed, joined by '\n'.</returns>
    public static string CollapseLines(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var raw in text.Split('\n'))
        {
            var line = Collapse(raw);
            if (line.Length == 0)
                continue;

            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(line);
        }

        return builder.ToString();
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

    #region Properties

    private static HtmlParser Parser => m_parser ??= CreateParser();

    #endregion
}
