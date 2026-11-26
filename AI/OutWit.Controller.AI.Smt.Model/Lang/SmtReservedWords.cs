using System.Collections.Frozen;

namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// The reserved words of SMT-LIB 2.6 (section 3.1): the general ones, which shape a term or a
/// sort wherever they stand, and the command names. A simple symbol may not be one of them; the
/// same text between bars is an ordinary symbol by the standard, and keeps its bars when written.
/// </summary>
public static class SmtReservedWords
{
    #region Constants

    private static readonly FrozenSet<string> GENERAL = new[]
    {
        "!", "_", "as", "BINARY", "DECIMAL", "exists", "HEXADECIMAL", "forall", "let", "match", "NUMERAL", "par", "STRING"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> COMMANDS = new[]
    {
        "assert", "check-sat", "check-sat-assuming", "declare-const", "declare-datatype", "declare-datatypes",
        "declare-fun", "declare-sort", "define-fun", "define-fun-rec", "define-funs-rec", "define-sort", "echo",
        "exit", "get-assertions", "get-assignment", "get-info", "get-model", "get-option", "get-proof",
        "get-unsat-assumptions", "get-unsat-core", "get-value", "pop", "push", "reset", "reset-assertions",
        "set-info", "set-logic", "set-option"
    }.ToFrozenSet(StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>
    /// Whether a text is one of the general reserved words (<c>let</c>, <c>forall</c>, <c>_</c>, <c>!</c> and the rest).
    /// </summary>
    /// <param name="text">A symbol's name.</param>
    /// <returns>True for a general reserved word.</returns>
    public static bool IsGeneral(string text)
    {
        return GENERAL.Contains(text);
    }

    /// <summary>
    /// Whether a text is the name of a command of the standard.
    /// </summary>
    /// <param name="text">A symbol's name.</param>
    /// <returns>True for a command name.</returns>
    public static bool IsCommand(string text)
    {
        return COMMANDS.Contains(text);
    }

    /// <summary>
    /// Whether a text is a reserved word of either kind.
    /// </summary>
    /// <param name="text">A symbol's name.</param>
    /// <returns>True for a reserved word.</returns>
    public static bool Contains(string text)
    {
        return GENERAL.Contains(text) || COMMANDS.Contains(text);
    }

    #endregion

    #region Properties

    /// <summary>The general reserved words: a set no caller can change.</summary>
    public static FrozenSet<string> General => GENERAL;

    /// <summary>The command names: a set no caller can change.</summary>
    public static FrozenSet<string> Commands => COMMANDS;

    #endregion
}
