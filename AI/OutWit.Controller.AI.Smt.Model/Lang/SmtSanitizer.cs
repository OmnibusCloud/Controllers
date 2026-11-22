namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// Takes the text of a problem and gives either the problem or a refusal, before any solver
/// sees the text. The same code looks at a problem wherever one is looked at, so a problem is
/// the same problem, with the same hash, everywhere.
/// <para>
/// A problem states something: it declares, defines and asserts. What asks a solver to act
/// (<c>check-sat</c>, <c>get-model</c>, <c>get-value</c>, <c>get-info</c>, <c>exit</c>) is
/// stripped and counted: the query decides what runs. Every other command is refused, by name
/// when it is known and for being unknown when it is not; no command is passed on unexamined.
/// </para>
/// <para>
/// A kept command is held to the standard's shape and to the rules of <see cref="SmtRefusalCodes"/>:
/// where good SMT-LIB would mean something else to the solver, the problem is refused, not
/// reinterpreted. A refusal names the line, the command, a fixed code and a reason; it never
/// carries the text. Nothing here throws for a bad text, and nothing recurses on nesting.
/// </para>
/// </summary>
public static class SmtSanitizer
{
    #region Constants

    /// <summary>The most names a problem may declare.</summary>
    public const int MAX_DECLARED_SYMBOLS = 100_000;

    /// <summary>The greatest index of an indexed identifier: the widest bit-vector.</summary>
    public const int MAX_INDEX = 65_536;

    /// <summary>The most characters of the text of one term.</summary>
    public const int TERM_CHARACTERS = 65_536;

    /// <summary>The most characters kept of a problem's <c>:source</c> and of its <c>:license</c>.</summary>
    public const int ATTRIBUTION_CHARACTERS = 1_024;

    /// <summary>The prefix of the controller's own names: no name a problem declares starts with it.</summary>
    public const string RESERVED_PREFIX = "oc!";

    private static readonly SmtReaderLimits TERM_LIMITS = new()
    {
        MaxCharacters = TERM_CHARACTERS,
        MaxTokens = SmtReaderLimits.PROBLEM_TOKENS,
        MaxDepth = SmtReaderLimits.PROBLEM_DEPTH,
        MaxNumeralDigits = SmtReaderLimits.PROBLEM_NUMERAL_DIGITS,
        MaxLiteralBits = SmtReaderLimits.PROBLEM_LITERAL_BITS,
        MaxStringCharacters = SmtReaderLimits.PROBLEM_STRING_CHARACTERS
    };

    #endregion

    #region Functions

    /// <summary>
    /// Sanitizes the text of one problem, read under <see cref="SmtReaderLimits.Problem"/>.
    /// </summary>
    /// <param name="text">The text of the problem: an SMT-LIB script.</param>
    /// <returns>The problem, or the refusal of the first thing in the text that breaks a rule.</returns>
    /// <exception cref="ArgumentNullException">There is no text.</exception>
    public static SmtSanitizerResult Sanitize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        IReadOnlyList<SmtExpression> forms;
        try
        {
            forms = SmtReader.Read(text, SmtReaderLimits.Problem);
        }
        catch (SmtSyntaxException fault)
        {
            return new SmtSanitizerResult(SmtRefusals.Of(fault));
        }

        var builder = new SmtProblemBuilder();
        foreach (var form in forms)
        {
            var refusal = builder.Take(form);
            if (refusal != null)
                return new SmtSanitizerResult(refusal);
        }

        // Written back, a text can be longer than it was: "(a)(b)" is "(a) (b)". A problem whose own
        // text would be refused is refused here, so that the canonical text always reads again.
        var problem = builder.Build();
        if (problem.Canonical.Length > SmtReaderLimits.PROBLEM_CHARACTERS)
            return new SmtSanitizerResult(TooLongWhenWritten(problem.Canonical.Length, SmtReaderLimits.PROBLEM_CHARACTERS));

        return new SmtSanitizerResult(problem);
    }

    /// <summary>
    /// Sanitizes the text of one term that stands beside a problem: a goal to prove, a term whose
    /// value is asked, a constant to project on. The text is exactly one s-expression of at most
    /// <see cref="TERM_CHARACTERS"/> characters, and its atoms are held to the rules a kept
    /// command's atoms are held to. What the term means is the solver's to check.
    /// </summary>
    /// <param name="text">The text of the term.</param>
    /// <returns>The term, or the refusal.</returns>
    /// <exception cref="ArgumentNullException">There is no text.</exception>
    public static SmtSanitizerTermResult SanitizeTerm(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        IReadOnlyList<SmtExpression> expressions;
        try
        {
            expressions = SmtReader.Read(text, TERM_LIMITS);
        }
        catch (SmtSyntaxException fault)
        {
            return new SmtSanitizerTermResult(SmtRefusals.Of(fault));
        }

        if (expressions.Count != 1)
        {
            var line = expressions.Count == 0 ? 1 : expressions[1].Line;
            var reason = expressions.Count == 0 ? "a term is one s-expression; the text holds none" : "a term is one s-expression; the text holds more than one";
            return new SmtSanitizerTermResult(SmtRefusals.Of(SmtRefusalCodes.NOT_A_TERM, line, "", reason));
        }

        var refusal = SmtTokenRules.Check(expressions[0], "");
        if (refusal != null)
            return new SmtSanitizerTermResult(refusal);

        var written = expressions[0].ToString().Length;
        return written > TERM_CHARACTERS
            ? new SmtSanitizerTermResult(TooLongWhenWritten(written, TERM_CHARACTERS))
            : new SmtSanitizerTermResult(expressions[0]);
    }

    private static SmtRefusalData TooLongWhenWritten(int characters, int allowed)
    {
        return SmtRefusals.Of(SmtRefusalCodes.TEXT_TOO_LONG, 1, "", $"written with one blank between two tokens the text has {characters} characters, more than the {allowed} allowed");
    }

    #endregion
}
