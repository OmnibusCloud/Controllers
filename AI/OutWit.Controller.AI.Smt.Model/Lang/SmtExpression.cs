using System.Text;

namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// One SMT-LIB 2.6 s-expression: a list of s-expressions, or an atom with its text. Immutable.
/// <para>
/// An atom's text is what the atom means, not how it was spelled: a symbol carries its name
/// without bars (<c>|a b|</c> is the symbol <c>a b</c>, and <c>|abc|</c> is the symbol
/// <c>abc</c>), a string its content with <c>""</c> already one quote. Every other atom carries
/// its token as written (<c>:status</c>, <c>42</c>, <c>1.50</c>, <c>#x0F</c>, <c>#b0101</c>).
/// </para>
/// <para>
/// Written back (<see cref="WriteTo"/>), an s-expression is one line of single-spaced tokens that
/// reads as the same s-expression again. Nothing here recurses on nesting: a term a hundred
/// thousand deep is read, written and compared without a deep call stack.
/// </para>
/// </summary>
public sealed class SmtExpression
{
    #region Constants

    private const char BAR = '|';
    private const char BACKSLASH = '\\';
    private const char QUOTE = '"';

    private static readonly IReadOnlyList<SmtExpression> NO_ITEMS = [];

    #endregion

    #region Constructors

    private SmtExpression(SmtExpressionKind kind, string text, IReadOnlyList<SmtExpression> items, int line, bool isQuoted)
    {
        Kind = kind;
        Text = text;
        Items = items;
        Line = line;
        IsQuoted = isQuoted;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Makes an atom that is neither a symbol nor a list: a keyword, a numeral, a decimal, a
    /// hexadecimal or binary literal with its token, or a string with its content.
    /// </summary>
    /// <param name="kind">The atom's kind.</param>
    /// <param name="text">The token, or a string's content.</param>
    /// <param name="line">The line it started on (1-based), 0 when made by a program.</param>
    /// <returns>The atom.</returns>
    /// <exception cref="ArgumentException">The kind is a list or a symbol.</exception>
    public static SmtExpression Atom(SmtExpressionKind kind, string text, int line = 0)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (kind is SmtExpressionKind.List or SmtExpressionKind.Symbol)
            throw new ArgumentException($"an atom of kind {kind} is made by its own factory", nameof(kind));

        return new SmtExpression(kind, text, NO_ITEMS, line, false);
    }

    /// <summary>
    /// Makes a symbol that was not written between bars: a simple symbol or a reserved word. A
    /// name a program made that is no simple symbol is written between bars.
    /// </summary>
    /// <param name="name">The symbol's name.</param>
    /// <param name="line">The line it started on (1-based), 0 when made by a program.</param>
    /// <returns>The symbol.</returns>
    /// <exception cref="ArgumentException">The name holds a bar or a backslash: no symbol can.</exception>
    public static SmtExpression Symbol(string name, int line = 0)
    {
        return new SmtExpression(SmtExpressionKind.Symbol, Writable(name), NO_ITEMS, line, false);
    }

    /// <summary>
    /// Makes a symbol that was written between bars. It is the same symbol as the simple one of
    /// the same name; when the name is a reserved word it stays between bars, where it is an
    /// ordinary symbol and not that word.
    /// </summary>
    /// <param name="name">The name without the bars.</param>
    /// <param name="line">The line it started on (1-based), 0 when made by a program.</param>
    /// <returns>The symbol.</returns>
    /// <exception cref="ArgumentException">The name holds a bar or a backslash: no symbol can.</exception>
    public static SmtExpression QuotedSymbol(string name, int line = 0)
    {
        return new SmtExpression(SmtExpressionKind.Symbol, Writable(name), NO_ITEMS, line, true);
    }

    /// <summary>
    /// Makes a list.
    /// </summary>
    /// <param name="items">The items, in order; the list keeps the collection it is given.</param>
    /// <param name="line">The line it started on (1-based), 0 when made by a program.</param>
    /// <returns>The list.</returns>
    public static SmtExpression List(IReadOnlyList<SmtExpression> items, int line = 0)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new SmtExpression(SmtExpressionKind.List, "", items, line, false);
    }

    /// <summary>
    /// Makes a list of the given items.
    /// </summary>
    /// <param name="items">The items, in order.</param>
    /// <returns>The list.</returns>
    public static SmtExpression List(params SmtExpression[] items)
    {
        return List((IReadOnlyList<SmtExpression>)items);
    }

    /// <summary>
    /// Whether this is the symbol of a name, however it was spelled.
    /// </summary>
    /// <param name="name">A symbol's name.</param>
    /// <returns>True for that symbol.</returns>
    public bool IsSymbol(string name)
    {
        return Kind == SmtExpressionKind.Symbol && string.Equals(Text, name, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether this is a list whose first item is the symbol of a name.
    /// </summary>
    /// <param name="head">A symbol's name.</param>
    /// <returns>True for such a list.</returns>
    public bool IsApplication(string head)
    {
        return Kind == SmtExpressionKind.List && Items.Count > 0 && Items[0].IsSymbol(head);
    }

    /// <summary>
    /// Whether another s-expression is the same one: the same shape, and in every place an atom of
    /// the same kind and text. A symbol between bars is the simple symbol of the same name, except
    /// where the name is a reserved word. The lines are not compared. Two s-expressions are the
    /// same exactly when they are written the same.
    /// </summary>
    /// <param name="other">The other s-expression.</param>
    /// <returns>True when the two are written the same.</returns>
    public bool Is(SmtExpression? other)
    {
        if (other == null)
            return false;

        var pending = new Stack<(SmtExpression Left, SmtExpression Right)>();
        pending.Push((this, other));
        while (pending.Count > 0)
        {
            var (left, right) = pending.Pop();
            if (ReferenceEquals(left, right))
                continue;

            if (left.Kind != right.Kind || left.Items.Count != right.Items.Count
                || !string.Equals(left.Text, right.Text, StringComparison.Ordinal))
                return false;

            if (left.Kind == SmtExpressionKind.Symbol && left.NeedsBars() != right.NeedsBars())
                return false;

            for (var index = 0; index < left.Items.Count; index++)
                pending.Push((left.Items[index], right.Items[index]));
        }

        return true;
    }

    /// <summary>
    /// Writes the s-expression in SMT-LIB syntax on one line of tokens, one space between two
    /// items of a list (a symbol or a string that holds a line break keeps it).
    /// </summary>
    /// <param name="builder">Where to write.</param>
    public void WriteTo(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var pending = new Stack<(SmtExpression Expression, int Next)>();
        pending.Push((this, -1));
        while (pending.Count > 0)
        {
            var (expression, next) = pending.Pop();
            if (expression.Kind != SmtExpressionKind.List)
            {
                expression.WriteAtom(builder);
                continue;
            }

            if (next < 0)
            {
                builder.Append('(');
                next = 0;
            }

            if (next >= expression.Items.Count)
            {
                builder.Append(')');
                continue;
            }

            if (next > 0)
                builder.Append(' ');

            pending.Push((expression, next + 1));
            pending.Push((expression.Items[next], -1));
        }
    }

    /// <summary>
    /// The s-expression as SMT-LIB text (<see cref="WriteTo"/>).
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        WriteTo(builder);
        return builder.ToString();
    }

    private void WriteAtom(StringBuilder builder)
    {
        switch (Kind)
        {
            case SmtExpressionKind.Symbol:
                if (NeedsBars())
                    builder.Append(BAR).Append(Text).Append(BAR);
                else
                    builder.Append(Text);
                break;
            case SmtExpressionKind.String:
                builder.Append(QUOTE).Append(Text.Replace("\"", "\"\"")).Append(QUOTE);
                break;
            default:
                builder.Append(Text);
                break;
        }
    }

    /// <summary>
    /// A name that is no simple symbol is written between bars, whoever made it. A simple name
    /// is written bare, except a reserved word that was written between bars: bare it would be
    /// that word.
    /// </summary>
    private bool NeedsBars()
    {
        if (!SmtLexicon.IsSimpleSymbol(Text))
            return true;

        return IsQuoted && SmtReservedWords.Contains(Text);
    }

    private static string Writable(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Contains(BAR) || name.Contains(BACKSLASH))
            throw new ArgumentException("a symbol's name holds no bar and no backslash", nameof(name));

        return name;
    }

    #endregion

    #region Properties

    /// <summary>What the s-expression is.</summary>
    public SmtExpressionKind Kind { get; }

    /// <summary>
    /// An atom's text: a symbol's name without bars, a string's content, any other atom's token.
    /// Empty for a list.
    /// </summary>
    public string Text { get; }

    /// <summary>A list's items; empty for an atom.</summary>
    public IReadOnlyList<SmtExpression> Items { get; }

    /// <summary>The line the s-expression started on (1-based); 0 when a program made it.</summary>
    public int Line { get; }

    /// <summary>Whether the symbol was written between bars.</summary>
    public bool IsQuoted { get; }

    /// <summary>Whether this is a list.</summary>
    public bool IsList => Kind == SmtExpressionKind.List;

    /// <summary>The name of the symbol a list starts with; null for an atom, an empty list, or a list that starts with anything else.</summary>
    public string? Head => Kind == SmtExpressionKind.List && Items.Count > 0 && Items[0].Kind == SmtExpressionKind.Symbol ? Items[0].Text : null;

    #endregion
}
