using System.Globalization;

namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// The rules every atom of a command the solver is given, and of a term, is held to (a
/// <c>set-info</c> never reaches the solver and is not held to them): the places where a text
/// that is good SMT-LIB means something else to the solver, or asks it for something of a size
/// no problem needs.
/// <list type="bullet">
/// <item>a general reserved word between bars is an ordinary symbol by the standard and the reserved word to the solver;</item>
/// <item>a symbol that starts with a minus sign and a digit is a symbol by the standard and a negative number to the solver;</item>
/// <item>a string literal holds printable ASCII, tabs and line breaks: the theory of strings takes any other character as an escape;</item>
/// <item>an index of an indexed identifier (a bit-vector's width, an extension, a repetition, a rotation) is at most the widest bit-vector.</item>
/// </list>
/// The walk keeps its place on a stack of its own, in the order of the text: the first atom that
/// breaks a rule is the one refused.
/// </summary>
internal static class SmtTokenRules
{
    #region Constants

    private const string INDEXED = "_";
    private const char MINUS = '-';
    private const char SPACE = ' ';
    private const char TILDE = '~';
    private const char ZERO = '0';
    private const int CODE_POINT_DIGITS = 4;
    private const string ESCAPE_OPEN = "\\u{";
    private const string ESCAPE_CLOSE = "}";

    private static readonly string MAX_INDEX_TEXT = SmtSanitizer.MAX_INDEX.ToString(CultureInfo.InvariantCulture);

    #endregion

    #region Functions

    /// <summary>
    /// Holds every atom of an s-expression to the rules.
    /// </summary>
    /// <param name="root">A command the solver is given, or a term.</param>
    /// <param name="command">The command's name for the refusal, or empty for a term.</param>
    /// <returns>The refusal of the first atom that breaks a rule, or null.</returns>
    public static SmtRefusalData? Check(SmtExpression root, string command)
    {
        if (!root.IsList)
            return CheckAtom(root, null, command);

        var open = new Stack<(SmtExpression List, int Next)>();
        open.Push((root, 0));
        while (open.Count > 0)
        {
            var (list, next) = open.Pop();
            if (next >= list.Items.Count)
                continue;

            open.Push((list, next + 1));

            var item = list.Items[next];
            if (item.IsList)
            {
                open.Push((item, 0));
                continue;
            }

            var refusal = CheckAtom(item, list, command);
            if (refusal != null)
                return refusal;
        }

        return null;
    }

    private static SmtRefusalData? CheckAtom(SmtExpression atom, SmtExpression? list, string command)
    {
        switch (atom.Kind)
        {
            case SmtExpressionKind.Symbol when atom.IsQuoted && SmtReservedWords.IsGeneral(atom.Text):
                return SmtRefusals.Of(SmtRefusalCodes.RESERVED_SYMBOL, atom.Line, command,
                    $"the reserved word '{atom.Text}' is written between bars: there it is an ordinary symbol by the standard and the reserved word to the solver, so it cannot be a name");

            case SmtExpressionKind.Symbol when ReadsAsNegativeNumber(atom.Text):
                return SmtRefusals.Of(SmtRefusalCodes.AMBIGUOUS_TOKEN, atom.Line, command,
                    $"'{SmtRefusals.Token(atom.Text)}' is a symbol by the standard and a negative number to the solver; a negative number is written (- 5)");

            case SmtExpressionKind.String when FirstOutsideAscii(atom.Text) is { } index:
                return SmtRefusals.Of(SmtRefusalCodes.NON_ASCII_STRING, atom.Line, command, WhyNotAscii(atom.Text, index));

            case SmtExpressionKind.Numeral when list != null && IsIndexed(list) && IsAboveMaxIndex(atom.Text):
                return SmtRefusals.Of(SmtRefusalCodes.INDEX_TOO_LARGE, atom.Line, command,
                    $"the index {SmtRefusals.Token(atom.Text)} is above the {SmtSanitizer.MAX_INDEX} allowed");

            default:
                return null;
        }
    }

    /// <summary>
    /// A name the writer writes without bars, a minus sign and a digit first: the solver reads a
    /// number there, between bars or not (written back, the bars are gone).
    /// </summary>
    private static bool ReadsAsNegativeNumber(string name)
    {
        return name.Length > 1 && name[0] == MINUS && char.IsAsciiDigit(name[1]) && SmtLexicon.IsSimpleSymbol(name);
    }

    private static int? FirstOutsideAscii(string content)
    {
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if ((character < SPACE || character > TILDE) && character is not ('\t' or '\n' or '\r'))
                return index;
        }

        return null;
    }

    private static string WhyNotAscii(string content, int index)
    {
        var character = content[index];
        var isPair = char.IsHighSurrogate(character) && index + 1 < content.Length && char.IsLowSurrogate(content[index + 1]);
        var code = (isPair ? char.ConvertToUtf32(character, content[index + 1]) : character).ToString("X", CultureInfo.InvariantCulture);

        return $"a string literal holds the character U+{code.PadLeft(CODE_POINT_DIGITS, ZERO)}: a character outside printable ASCII is written as the escape {ESCAPE_OPEN}{code}{ESCAPE_CLOSE}";
    }

    private static bool IsIndexed(SmtExpression list)
    {
        var head = list.Items[0];
        return head.Kind == SmtExpressionKind.Symbol && !head.IsQuoted && string.Equals(head.Text, INDEXED, StringComparison.Ordinal);
    }

    /// <summary>
    /// A numeral has no leading zero, so the longer one is the greater, and two of one length
    /// compare as their texts do: a numeral of four thousand digits is compared without being parsed.
    /// </summary>
    private static bool IsAboveMaxIndex(string numeral)
    {
        if (numeral.Length != MAX_INDEX_TEXT.Length)
            return numeral.Length > MAX_INDEX_TEXT.Length;

        return string.CompareOrdinal(numeral, MAX_INDEX_TEXT) > 0;
    }

    #endregion
}
