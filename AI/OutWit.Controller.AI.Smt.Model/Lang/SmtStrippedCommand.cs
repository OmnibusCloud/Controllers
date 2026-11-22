namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// A command that was in the text and is not in the problem: where it started and its name,
/// for a preview that shows the text with such commands struck through. Immutable.
/// </summary>
public sealed class SmtStrippedCommand
{
    #region Constructors

    /// <summary>
    /// Creates the record of a stripped command.
    /// </summary>
    /// <param name="line">The line the command started on (1-based).</param>
    /// <param name="command">The command's name.</param>
    public SmtStrippedCommand(int line, string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Line = line;
        Command = command;
    }

    #endregion

    #region Properties

    /// <summary>The line the command started on (1-based).</summary>
    public int Line { get; }

    /// <summary>The command's name (<c>check-sat</c>, <c>exit</c>, ...).</summary>
    public string Command { get; }

    #endregion
}
