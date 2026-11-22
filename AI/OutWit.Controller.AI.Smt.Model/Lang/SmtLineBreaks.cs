using System.Text;

namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// The line breaks inside a piece of information (<c>set-info</c>). Nearly every problem of a
/// public collection carries its source as a text of several lines, and a file is the same
/// problem whether its lines end with a line feed or with a carriage return and a line feed. So
/// in a string or a quoted symbol of a <c>set-info</c> a line break is written as one line feed,
/// however the file wrote it. No <c>set-info</c> reaches the solver, so nothing the solver is
/// given changes by this.
/// <para>
/// In every other command a line break inside a string or a quoted symbol is left as it is: there
/// it is part of a value or of a name, and two texts that differ in it are two problems.
/// </para>
/// </summary>
internal static class SmtLineBreaks
{
    #region Constants

    private const char CARRIAGE_RETURN = '\r';
    private const char LINE_FEED = '\n';

    #endregion

    #region Functions

    /// <summary>
    /// Writes every line break inside the strings and the quoted symbols of a command as one line feed.
    /// </summary>
    /// <param name="command">A <c>set-info</c> command.</param>
    /// <returns>The command itself when it holds no carriage return before a line feed; the same command with such line breaks rewritten otherwise.</returns>
    public static SmtExpression Normalize(SmtExpression command)
    {
        if (!command.IsList || !HoldsWindowsLineBreak(command))
            return command;

        // Rebuilt from the leaves up, on a stack of its own: the items a list has so far say where it is.
        var open = new Stack<(SmtExpression List, List<SmtExpression> Items)>();
        open.Push((command, new List<SmtExpression>(command.Items.Count)));
        while (true)
        {
            var (list, items) = open.Peek();
            if (items.Count == list.Items.Count)
            {
                open.Pop();
                var rebuilt = SmtExpression.List(items, list.Line);
                if (open.Count == 0)
                    return rebuilt;

                open.Peek().Items.Add(rebuilt);
                continue;
            }

            var item = list.Items[items.Count];
            if (item.IsList)
                open.Push((item, new List<SmtExpression>(item.Items.Count)));
            else
                items.Add(NormalizeAtom(item));
        }
    }

    private static bool HoldsWindowsLineBreak(SmtExpression command)
    {
        var open = new Stack<(SmtExpression List, int Next)>();
        open.Push((command, 0));
        while (open.Count > 0)
        {
            var (list, next) = open.Pop();
            if (next >= list.Items.Count)
                continue;

            open.Push((list, next + 1));

            var item = list.Items[next];
            if (item.IsList)
                open.Push((item, 0));
            else if (NeedsNormalizing(item))
                return true;
        }

        return false;
    }

    private static bool NeedsNormalizing(SmtExpression atom)
    {
        return atom.Kind is SmtExpressionKind.String or SmtExpressionKind.Symbol && WindowsLineBreak(atom.Text, 0) >= 0;
    }

    private static SmtExpression NormalizeAtom(SmtExpression atom)
    {
        if (!NeedsNormalizing(atom))
            return atom;

        // A symbol that holds a line break was written between bars: no simple symbol holds one.
        var text = WithLineFeeds(atom.Text);
        return atom.Kind == SmtExpressionKind.String
            ? SmtExpression.Atom(SmtExpressionKind.String, text, atom.Line)
            : SmtExpression.QuotedSymbol(text, atom.Line);
    }

    /// <summary>
    /// Drops every run of carriage returns that a line feed follows. What is left holds no
    /// carriage return before a line feed, so the text written back reads as itself.
    /// </summary>
    private static string WithLineFeeds(string text)
    {
        var builder = new StringBuilder(text.Length);
        var position = 0;
        while (true)
        {
            var at = WindowsLineBreak(text, position);
            if (at < 0)
                break;

            builder.Append(text, position, at - position);
            position = at;
            while (text[position] == CARRIAGE_RETURN)
                position++;
        }

        return builder.Append(text, position, text.Length - position).ToString();
    }

    /// <summary>The start of the first run of carriage returns that a line feed follows, from a position on; -1 when there is none.</summary>
    private static int WindowsLineBreak(string text, int from)
    {
        var start = -1;
        for (var index = from; index < text.Length; index++)
        {
            var character = text[index];
            if (character == CARRIAGE_RETURN)
            {
                if (start < 0)
                    start = index;
            }
            else if (character == LINE_FEED && start >= 0)
            {
                return start;
            }
            else
            {
                start = -1;
            }
        }

        return -1;
    }

    #endregion
}
