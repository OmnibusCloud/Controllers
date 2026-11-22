using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// One problem as the sanitizer took it (<see cref="SmtSanitizer.Sanitize"/>): the commands that
/// state it, in the order of the text, and what the controller reads off them. Immutable.
/// <para>
/// A problem has two texts. <see cref="Canonical"/> is every kept command, one a line: what a
/// person is shown as the problem, and what <see cref="Hash"/> names. <see cref="SolverText"/>
/// is the same without any <c>set-info</c>: the solver acts on some pieces of information (a
/// <c>:status</c> that disagrees with its verdict is an error to it, <c>:smt-lib-version</c>
/// changes how it reads), so it is given none.
/// </para>
/// <para>
/// The canonical text is a fixed point: sanitized again, it gives the same problem. It depends
/// on the kept commands' tokens and on nothing else: not on comments, on blanks or line endings
/// between tokens, on bars around a name that needs none, on stripped commands. Nor does it
/// depend on how a file ends the lines of a text inside a <c>set-info</c>: there a line break is
/// written as one line feed. In every other command a line break inside a string or a quoted
/// symbol is part of that token.
/// </para>
/// </summary>
public sealed class SmtProblem
{
    #region Constructors

    internal SmtProblem(
        List<SmtExpression> commands,
        List<SmtExpression> solverCommands,
        string? logic,
        SmtProblemStatus status,
        string source,
        string license,
        List<SmtDeclaredConstant> constants,
        List<SmtDeclaredFunction> functions,
        int assertionCount,
        int declaredSymbolCount,
        Dictionary<string, int> stripped,
        List<SmtStrippedCommand> strippedLines)
    {
        Commands = commands.AsReadOnly();
        SolverCommands = solverCommands.AsReadOnly();
        Logic = logic;
        Status = status;
        Source = source;
        License = license;
        Constants = constants.AsReadOnly();
        Functions = functions.AsReadOnly();
        AssertionCount = assertionCount;
        DeclaredSymbolCount = declaredSymbolCount;
        Stripped = new ReadOnlyDictionary<string, int>(stripped);
        StrippedLines = strippedLines.AsReadOnly();

        Canonical = SmtScripts.Text(Commands);
        SolverText = SmtScripts.Text(SolverCommands);
        Hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical)));
    }

    #endregion

    #region Properties

    /// <summary>Every kept command, in the order of the text; <c>set-info</c> included.</summary>
    public IReadOnlyList<SmtExpression> Commands { get; }

    /// <summary>The commands the solver is given: <see cref="Commands"/> without any <c>set-info</c>.</summary>
    public IReadOnlyList<SmtExpression> SolverCommands { get; }

    /// <summary>
    /// The text of <see cref="Commands"/>: one command a line as <see cref="SmtExpression.WriteTo"/>
    /// writes it, every line ended by a line feed.
    /// </summary>
    public string Canonical { get; }

    /// <summary>The text of <see cref="SolverCommands"/>, written as <see cref="Canonical"/> is.</summary>
    public string SolverText { get; }

    /// <summary>The SHA-256 of the UTF-8 bytes of <see cref="Canonical"/>, in lower-case hexadecimal.</summary>
    public string Hash { get; }

    /// <summary>The logic the first <c>set-logic</c> names (the one a solver acts on); null when the problem names none.</summary>
    public string? Logic { get; }

    /// <summary>
    /// The verdict the problem expects: its last <c>:status</c>, when that is the symbol
    /// <c>sat</c> or <c>unsat</c>.
    /// </summary>
    public SmtProblemStatus Status { get; }

    /// <summary>
    /// Where the problem comes from: the string or the symbol of its last <c>:source</c>, cut to
    /// <see cref="SmtSanitizer.ATTRIBUTION_CHARACTERS"/> characters; empty when it has none.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// The problem's licence: the string or the symbol of its last <c>:license</c>, cut to
    /// <see cref="SmtSanitizer.ATTRIBUTION_CHARACTERS"/> characters; empty when it has none.
    /// </summary>
    public string License { get; }

    /// <summary>Every declared constant with its sort, in order: each <c>declare-const</c>, and each <c>declare-fun</c> of no arguments.</summary>
    public IReadOnlyList<SmtDeclaredConstant> Constants { get; }

    /// <summary>Every declared function of one argument or more with its signature, in order.</summary>
    public IReadOnlyList<SmtDeclaredFunction> Functions { get; }

    /// <summary>The number of <c>assert</c> commands.</summary>
    public int AssertionCount { get; }

    /// <summary>
    /// The number of names the problem declares: constants, functions and sorts (declared or
    /// defined), datatypes, their constructors and their selectors.
    /// </summary>
    public int DeclaredSymbolCount { get; }

    /// <summary>The commands that were stripped, counted by name.</summary>
    public IReadOnlyDictionary<string, int> Stripped { get; }

    /// <summary>Every stripped command with the line it started on, in the order of the text.</summary>
    public IReadOnlyList<SmtStrippedCommand> StrippedLines { get; }

    #endregion
}
