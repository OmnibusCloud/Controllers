using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// The way a problem really comes to the evaluator: its text through the sanitizer, the problem
/// the sanitizer took into the validator.
/// </summary>
[TestFixture]
public class SmtModelValidatorSanitizedTests
{
    #region Constants

    /// <summary>
    /// The fixtures the sanitizer refuses, with the code: the older form of declare-datatypes is
    /// the solver's shorthand, not the standard's.
    /// </summary>
    private static readonly Dictionary<string, string> REFUSED = new(StringComparer.Ordinal)
    {
        ["dt-mutual-parametric"] = SmtRefusalCodes.COMMAND_MALFORMED
    };

    #endregion

    #region Sanitized Tests

    [Test]
    public void ValidatorTakesTheProblemTheSanitizerTookTest()
    {
        // A problem as a file holds it: information about itself between its commands, a
        // constant with the name of a command, and the commands that ask a solver to act.
        var problem = Sanitized();

        var validator = new SmtModelValidator(problem);
        var validated = validator.Validate(SmtCheck.Model("((define-fun reset () Int 4) (define-fun x () Int 6))", "((reset 4) (x 6))"), SmtEvaluationBudget.Default);
        var failed = validator.Validate(SmtCheck.Model("((define-fun reset () Int 4) (define-fun x () Int 5))"), SmtEvaluationBudget.Default);
        var partial = validator.Validate(SmtCheck.Model(null, "((reset 4))"), SmtEvaluationBudget.Default);

        Assert.That(problem.Stripped.Keys, Is.EquivalentTo(new[] { "check-sat", "get-model", "exit" }));
        Assert.That(validator.Assertions, Is.EqualTo(problem.AssertionCount));
        Assert.That(validator.Declared, Is.EqualTo(problem.Constants.Count + problem.Functions.Count));

        Assert.That(validated.Class, Is.EqualTo(SmtValidationClass.Validated), validated.ToString());
        Assert.That(validated.Defined, Is.EqualTo(2));
        Assert.That(failed.Class, Is.EqualTo(SmtValidationClass.Failed), failed.ToString());
        Assert.That(failed.Detail, Is.EqualTo("2"));
        Assert.That(partial.Class, Is.EqualTo(SmtValidationClass.Partial), partial.ToString());
        Assert.That(partial.Reason, Is.EqualTo(SmtValidationReasons.COMPLETED_SYMBOL));
    }

    [Test]
    public void ValidatorOfAProblemIsTheValidatorOfItsCommandsTest()
    {
        // With set-info or without it: what a problem says about itself changes no validation.
        var problem = Sanitized();
        var model = SmtCheck.Model("((define-fun reset () Int 4) (define-fun x () Int 6))");

        var byProblem = new SmtModelValidator(problem).Validate(model, SmtEvaluationBudget.Default);
        var byCommands = new SmtModelValidator(problem.Commands).Validate(model, SmtEvaluationBudget.Default);
        var bySolverCommands = new SmtModelValidator(problem.SolverCommands).Validate(model, SmtEvaluationBudget.Default);

        Assert.That(problem.Commands, Has.Count.GreaterThan(problem.SolverCommands.Count));
        Assert.That(byCommands.Is(byProblem), Is.True);
        Assert.That(bySolverCommands.Is(byProblem), Is.True);
        Assert.That(SmtClosedScript.Build(problem.Commands, model).ToString(), Is.EqualTo(SmtClosedScript.Build(problem.SolverCommands, model).ToString()));
    }

    [Test]
    public void ProblemWrittenBackIsCheckedLikeTheProblemTest()
    {
        // The canonical text is the problem: read again, it validates the same model the same way.
        var problem = Sanitized();
        var again = SmtSanitizer.Sanitize(problem.Canonical).Problem!;
        var model = SmtCheck.Model("((define-fun reset () Int 4) (define-fun x () Int 6))");

        Assert.That(again.Hash, Is.EqualTo(problem.Hash));
        Assert.That(new SmtModelValidator(again).Validate(model, SmtEvaluationBudget.Default).Is(new SmtModelValidator(problem).Validate(model, SmtEvaluationBudget.Default)), Is.True);
    }

    [Test]
    public void EveryFixtureTheSanitizerTakesIsValidatedThroughItTest()
    {
        var refused = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fixture in SmtFixtures.All)
        {
            var result = SmtSanitizer.Sanitize(fixture.Problem);
            if (!result.IsAccepted)
            {
                refused[fixture.Name] = result.Refusal!.Code;
                continue;
            }

            var model = SmtCheck.Model(fixture.Model, fixture.Values);
            var validation = new SmtModelValidator(result.Problem).Validate(model, SmtEvaluationBudget.Default);

            Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), $"{fixture.Name}: {validation}");
            Assert.That(validation.Is(SmtCheck.Validate(fixture.Problem, fixture.Model, fixture.Values)), Is.True, fixture.Name);
        }

        Assert.That(refused, Is.EquivalentTo(REFUSED));
    }

    #endregion

    #region Tools

    private static SmtProblem Sanitized()
    {
        var text = "(set-info :smt-lib-version 2.6)\n"
                   + "(set-info :source |written for a test by Ren" + (char)0xE9 + ", with (parentheses) and an (assert false) inside|)\n"
                   + "(set-info :status sat)\n"
                   + "(set-logic QF_LIA)\n"
                   + "(declare-const reset Int) ; the name of a command is a name\n"
                   + "(declare-const x Int)\n"
                   + "(set-info :note \"between the commands: (declare-const y Int)\")\n"
                   + "(assert (> reset 3))\n"
                   + "(assert (= (+ reset x) 10))\n"
                   + "(check-sat)\n"
                   + "(get-model)\n"
                   + "(exit)\n";

        var result = SmtSanitizer.Sanitize(text);

        Assert.That(result.IsAccepted, Is.True, () => $"{result.Refusal?.Code}: {result.Refusal?.Reason}");
        return result.Problem!;
    }

    #endregion
}
