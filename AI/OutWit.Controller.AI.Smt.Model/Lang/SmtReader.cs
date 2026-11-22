using System.Text;

namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// The reader of SMT-LIB 2.6 text: a script, a term, a model a solver printed. It takes the
/// standard's lexicon and nothing beside it, so that what it reads is what any conforming reader
/// reads, and it refuses the places where readers are known to differ:
/// <list type="bullet">
/// <item>blanks are the space, the tab, the line feed and the carriage return, and no other character;</item>
/// <item>a bare token is of printable ASCII, and is a simple symbol, a keyword, a numeral, a decimal,
/// or a hexadecimal or binary literal in full (<c>12abc</c>, <c>007</c>, <c>#x</c> and <c>a'b</c> are none);</item>
/// <item>a quoted symbol holds no backslash (the standard forbids it, and a solver that takes it
/// for an escape ends the symbol at another bar than the standard does);</item>
/// <item>a string literal or a quoted symbol holds no control character but the tab and the line
/// breaks, and no half of a surrogate pair.</item>
/// </list>
/// A comment runs from a semicolon to the end of its line and is dropped, whatever it holds.
/// <para>
/// The limits are counted while reading (<see cref="SmtReaderLimits"/>). Nesting is kept on a
/// stack of the reader's own, not on the call stack: how deep a text nests is bounded by its
/// limit, never by the thread.
/// </para>
/// </summary>
public sealed class SmtReader
{
    #region Constants

    private const char OPEN = '(';
    private const char CLOSE = ')';
    private const char QUOTE = '"';
    private const char BAR = '|';
    private const char BACKSLASH = '\\';
    private const char COMMENT = ';';
    private const char SPACE = ' ';
    private const char TAB = '\t';
    private const char CARRIAGE_RETURN = '\r';
    private const char LINE_FEED = '\n';
    private const char DELETE = (char)127;

    #endregion

    #region Fields

    private readonly string m_text;
    private readonly SmtReaderLimits m_limits;
    private int m_position;
    private int m_line = 1;
    private int m_lineStart;
    private int m_tokens;

    #endregion

    #region Constructors

    private SmtReader(string text, SmtReaderLimits limits)
    {
        m_text = text;
        m_limits = limits;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads every top-level s-expression of a text.
    /// </summary>
    /// <param name="text">SMT-LIB text.</param>
    /// <param name="limits">How much to take before refusing.</param>
    /// <returns>The top-level s-expressions, in order; none for a text of blanks and comments.</returns>
    /// <exception cref="SmtSyntaxException">The text is not SMT-LIB, or it is past a limit.</exception>
    public static IReadOnlyList<SmtExpression> Read(string text, SmtReaderLimits limits)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(limits);

        if (text.Length > limits.MaxCharacters)
            throw new SmtSyntaxException(SmtSyntaxFault.TextTooLong, 1, 1, $"the text has {text.Length} characters, more than the {limits.MaxCharacters} allowed");

        return new SmtReader(text, limits).ReadAll();
    }

    private List<SmtExpression> ReadAll()
    {
        var top = new List<SmtExpression>();
        var open = new Stack<(List<SmtExpression> Items, int Line, int Column)>();

        while (true)
        {
            SkipBlanksAndComments();
            if (m_position >= m_text.Length)
                break;

            var character = m_text[m_position];
            if (character == OPEN)
            {
                CountToken();
                if (open.Count >= m_limits.MaxDepth)
                    throw Refuse(SmtSyntaxFault.NestingTooDeep, $"lists are nested deeper than the {m_limits.MaxDepth} allowed");

                open.Push((new List<SmtExpression>(), m_line, Column));
                m_position++;
                continue;
            }

            if (character == CLOSE)
            {
                CountToken();
                if (open.Count == 0)
                    throw Refuse(SmtSyntaxFault.UnbalancedParenthesis, "a closing parenthesis with no list open");

                m_position++;
                var (items, line, _) = open.Pop();
                Add(SmtExpression.List(items, line), open, top);
                continue;
            }

            Add(ReadAtom(), open, top);
        }

        if (open.Count > 0)
        {
            var (_, line, column) = open.Peek();
            throw new SmtSyntaxException(SmtSyntaxFault.UnbalancedParenthesis, line, column, "a list is not closed");
        }

        return top;
    }

    private static void Add(SmtExpression expression, Stack<(List<SmtExpression> Items, int Line, int Column)> open, List<SmtExpression> top)
    {
        if (open.Count == 0)
            top.Add(expression);
        else
            open.Peek().Items.Add(expression);
    }

    private void SkipBlanksAndComments()
    {
        while (m_position < m_text.Length)
        {
            var character = m_text[m_position];
            if (character == COMMENT)
            {
                // To the end of the line, whatever is there: nothing of a comment is kept.
                while (m_position < m_text.Length && m_text[m_position] != LINE_FEED)
                    m_position++;
            }
            else if (character == LINE_FEED)
            {
                NewLine();
            }
            else if (SmtLexicon.IsBlank(character))
            {
                m_position++;
            }
            else
            {
                return;
            }
        }
    }

    private SmtExpression ReadAtom()
    {
        CountToken();

        return m_text[m_position] switch
        {
            QUOTE => ReadString(),
            BAR => ReadQuotedSymbol(),
            _ => ReadBareToken()
        };
    }

    private SmtExpression ReadString()
    {
        var line = m_line;
        var column = Column;
        var content = new StringBuilder();

        m_position++;
        while (true)
        {
            if (m_position >= m_text.Length)
                throw new SmtSyntaxException(SmtSyntaxFault.UnclosedLiteral, line, column, "a string literal is not closed");

            if (m_text[m_position] == QUOTE)
            {
                m_position++;
                if (m_position >= m_text.Length || m_text[m_position] != QUOTE)
                    return SmtExpression.Atom(SmtExpressionKind.String, content.ToString(), line);

                // Two quotes are one quote of the content.
                m_position++;
                content.Append(QUOTE);
            }
            else
            {
                var start = m_position;
                TakeLiteralCharacter();
                content.Append(m_text, start, m_position - start);
            }

            if (content.Length > m_limits.MaxStringCharacters)
                throw new SmtSyntaxException(SmtSyntaxFault.StringTooLong, line, column, $"a string literal is longer than the {m_limits.MaxStringCharacters} characters allowed");
        }
    }

    private SmtExpression ReadQuotedSymbol()
    {
        var line = m_line;
        var column = Column;

        m_position++;
        var start = m_position;
        while (true)
        {
            if (m_position >= m_text.Length)
                throw new SmtSyntaxException(SmtSyntaxFault.UnclosedLiteral, line, column, "a quoted symbol is not closed");

            var character = m_text[m_position];
            if (character == BAR)
            {
                var name = m_text.Substring(start, m_position - start);
                m_position++;
                return SmtExpression.QuotedSymbol(name, line);
            }

            if (character == BACKSLASH)
                throw Refuse(SmtSyntaxFault.BadCharacter, "a backslash in a quoted symbol");

            TakeLiteralCharacter();
        }
    }

    /// <summary>
    /// Steps over one character of a string literal or of a quoted symbol: a printable character,
    /// a blank, or a whole surrogate pair.
    /// </summary>
    private void TakeLiteralCharacter()
    {
        var character = m_text[m_position];
        if (character == LINE_FEED)
        {
            NewLine();
            return;
        }

        if (char.IsHighSurrogate(character))
        {
            if (m_position + 1 >= m_text.Length || !char.IsLowSurrogate(m_text[m_position + 1]))
                throw Refuse(SmtSyntaxFault.BadCharacter, "half of a surrogate pair");

            m_position += 2;
            return;
        }

        if (char.IsLowSurrogate(character))
            throw Refuse(SmtSyntaxFault.BadCharacter, "half of a surrogate pair");

        if (character == DELETE || (character < SPACE && character is not (TAB or CARRIAGE_RETURN)))
            throw Refuse(SmtSyntaxFault.BadCharacter, $"{SmtLexicon.Describe(character)} in a string literal or a quoted symbol");

        m_position++;
    }

    private SmtExpression ReadBareToken()
    {
        var line = m_line;
        var column = Column;
        var start = m_position;

        while (m_position < m_text.Length && !SmtLexicon.IsDelimiter(m_text[m_position]))
        {
            var character = m_text[m_position];
            if (character < SPACE || character >= DELETE)
                throw Refuse(SmtSyntaxFault.BadCharacter, $"{SmtLexicon.Describe(character)} outside a string literal and a quoted symbol");

            m_position++;
        }

        return SmtBareToken.Classify(m_text.AsSpan(start, m_position - start), m_limits, line, column);
    }

    #endregion

    #region Tools

    private void NewLine()
    {
        m_position++;
        m_line++;
        m_lineStart = m_position;
    }

    private void CountToken()
    {
        if (++m_tokens > m_limits.MaxTokens)
            throw Refuse(SmtSyntaxFault.TooManyTokens, $"the text has more than the {m_limits.MaxTokens} tokens allowed");
    }

    private SmtSyntaxException Refuse(SmtSyntaxFault fault, string reason)
    {
        return new SmtSyntaxException(fault, m_line, Column, reason);
    }

    #endregion

    #region Properties

    private int Column => m_position - m_lineStart + 1;

    #endregion
}
