namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// A text the reader refused: what is wrong (<see cref="Fault"/>), where, and a sentence a person
/// can act on. The message starts with the place and never quotes more of the text than a token.
/// </summary>
public sealed class SmtSyntaxException : FormatException
{
    #region Constructors

    /// <summary>
    /// Creates the refusal of a text.
    /// </summary>
    /// <param name="fault">What is wrong.</param>
    /// <param name="line">The line it is on (1-based).</param>
    /// <param name="column">The column it starts at (1-based, in characters).</param>
    /// <param name="reason">What is wrong, in words, without the place.</param>
    public SmtSyntaxException(SmtSyntaxFault fault, int line, int column, string reason)
        : base($"line {line}, column {column}: {reason}")
    {
        Fault = fault;
        Line = line;
        Column = column;
        Reason = reason;
    }

    #endregion

    #region Properties

    /// <summary>What is wrong with the text.</summary>
    public SmtSyntaxFault Fault { get; }

    /// <summary>The line the fault is on (1-based).</summary>
    public int Line { get; }

    /// <summary>The column the fault starts at (1-based, in characters).</summary>
    public int Column { get; }

    /// <summary>What is wrong, in words, without the place.</summary>
    public string Reason { get; }

    #endregion
}
