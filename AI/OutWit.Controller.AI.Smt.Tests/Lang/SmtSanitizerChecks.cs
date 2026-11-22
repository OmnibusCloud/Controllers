using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// What the sanitizer's tests ask of every outcome: a text is taken or refused, never both; a
/// refusal is one of the contract's and shows no more of the text than a token; a problem's
/// canonical text is a fixed point and its solver text holds nothing but what states a problem.
/// </summary>
internal static class SmtSanitizerChecks
{
    #region Constants

    /// <summary>The longest reason the sanitizer writes is a shape, a few hundred characters: a reason of thousands carries text.</summary>
    public const int MAX_REASON_CHARACTERS = 400;

    /// <summary>The names of the commands the solver is given, as the tests know them: the kept commands without <c>set-info</c>.</summary>
    public static readonly string[] SOLVER_COMMANDS =
    [
        "set-logic", "declare-sort", "define-sort", "declare-const", "declare-fun", "define-fun", "define-fun-rec",
        "define-funs-rec", "declare-datatype", "declare-datatypes", "assert"
    ];

    #endregion

    #region Functions

    /// <summary>Sanitizes a text that must be taken.</summary>
    /// <param name="text">The text of a problem.</param>
    /// <returns>The problem.</returns>
    public static SmtProblem Accept(string text)
    {
        var result = SmtSanitizer.Sanitize(text);

        Assert.That(result.Refusal, Is.Null, () => $"refused: {result.Refusal?.Code}, line {result.Refusal?.Line}: {result.Refusal?.Reason}");
        Assert.That(result.Problem, Is.Not.Null);
        Assert.That(result.IsAccepted, Is.True);

        return result.Problem!;
    }

    /// <summary>Sanitizes a text that must be refused with a code.</summary>
    /// <param name="text">The text of a problem.</param>
    /// <param name="code">The expected code.</param>
    /// <returns>The refusal.</returns>
    public static SmtRefusalData Refuse(string text, string code)
    {
        var result = SmtSanitizer.Sanitize(text);

        Assert.That(result.Problem, Is.Null, "the text was taken");
        Assert.That(result.IsAccepted, Is.False);
        Assert.That(result.Refusal, Is.Not.Null);
        Assert.That(result.Refusal!.Code, Is.EqualTo(code), result.Refusal.Reason);
        HoldRefusal(result.Refusal);

        return result.Refusal;
    }

    /// <summary>Holds a refusal to the contract: a known code, a reason, no file, and no more of the text than a token.</summary>
    /// <param name="refusal">The refusal.</param>
    public static void HoldRefusal(SmtRefusalData refusal)
    {
        Assert.That(SmtRefusalCodes.All, Does.Contain(refusal.Code));
        Assert.That(refusal.File, Is.Empty);
        Assert.That(refusal.Line, Is.GreaterThanOrEqualTo(1));
        Assert.That(refusal.Command.Length, Is.LessThanOrEqualTo(64));
        Assert.That(refusal.Reason, Is.Not.Empty);
        Assert.That(refusal.Reason.Length, Is.LessThanOrEqualTo(MAX_REASON_CHARACTERS), refusal.Reason);
    }

    /// <summary>
    /// Holds a problem to the two properties every accepted text has: the canonical text and the
    /// solver text are fixed points, and the solver text holds nothing but the commands that
    /// state a problem, their names written bare.
    /// </summary>
    /// <param name="problem">The problem.</param>
    public static void HoldProperties(SmtProblem problem)
    {
        var again = SmtSanitizer.Sanitize(problem.Canonical);
        Assert.That(again.Refusal, Is.Null, () => $"the canonical text is refused: {again.Refusal?.Code}, {again.Refusal?.Reason}");
        Assert.That(again.Problem!.Canonical, Is.EqualTo(problem.Canonical));
        Assert.That(again.Problem.Hash, Is.EqualTo(problem.Hash));
        Assert.That(again.Problem.SolverText, Is.EqualTo(problem.SolverText));
        Assert.That(again.Problem.Stripped, Is.Empty);
        Assert.That(Facts(again.Problem), Is.EqualTo(Facts(problem)));

        var solver = SmtSanitizer.Sanitize(problem.SolverText);
        Assert.That(solver.Refusal, Is.Null, () => $"the solver text is refused: {solver.Refusal?.Code}, {solver.Refusal?.Reason}");
        Assert.That(solver.Problem!.SolverText, Is.EqualTo(problem.SolverText));
        Assert.That(solver.Problem.Canonical, Is.EqualTo(problem.SolverText), "a text without set-info is its own solver text");

        var commands = SmtReader.Read(problem.SolverText, SmtReaderLimits.Problem);
        Assert.That(commands, Has.Count.EqualTo(problem.SolverCommands.Count));
        foreach (var command in commands)
        {
            Assert.That(SOLVER_COMMANDS, Does.Contain(command.Head));
            Assert.That(command.Items[0].IsQuoted, Is.False);
        }
    }

    /// <summary>What a problem says of itself, as one comparable text.</summary>
    private static string Facts(SmtProblem problem)
    {
        var constants = string.Join(",", problem.Constants.Select(constant => $"{constant.Symbol}:{constant.Sort}"));
        var functions = string.Join(",", problem.Functions.Select(function => $"{function.Symbol}:{string.Join(" ", function.Parameters)}:{function.Sort}"));

        return $"{problem.Logic}|{problem.Status}|{problem.Source}|{problem.License}|{problem.AssertionCount}|{problem.DeclaredSymbolCount}|{problem.Commands.Count}|{problem.SolverCommands.Count}|{constants}|{functions}";
    }

    #endregion
}
