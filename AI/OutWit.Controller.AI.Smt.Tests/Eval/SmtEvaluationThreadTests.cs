using System.Runtime.InteropServices;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// What the thread of a validation costs. The stack it reserves is sized for the nesting
/// ceiling; what the process really takes for it is only what a term nests. The numbers are
/// printed for whoever sizes a server, on every platform the tests run on.
/// </summary>
[TestFixture]
public class SmtEvaluationThreadTests
{
    #region Stack Tests

    [Test]
    public void StackIsSizedForTheCeilingTheValidationCountsAgainstTest()
    {
        var level = SmtEvaluationThread.BYTES_PER_LEVEL;
        var floor = SmtEvaluationThread.BASE_BYTES;

        Assert.That(SmtEvaluationThread.StackBytes(1), Is.EqualTo(floor + level));
        Assert.That(SmtEvaluationThread.StackBytes(1000), Is.EqualTo(floor + 1000 * level));
        Assert.That(SmtEvaluationThread.StackBytes(SmtEvaluationBudget.DEFAULT_DEPTH), Is.EqualTo(floor + 32_768 * level));
        Assert.That(SmtEvaluationThread.StackBytes(0), Is.EqualTo(floor + level));
        Assert.That(SmtEvaluationThread.StackBytes(-7), Is.EqualTo(floor + level));
        Assert.That(SmtEvaluationThread.StackBytes(int.MaxValue), Is.EqualTo(SmtEvaluationThread.StackBytes(SmtEvaluationBudget.DEPTH_CEILING)));
    }

    [Test]
    public void SmallValidationDoesNotCommitItsWholeReservedStackTest()
    {
        var budget = SmtEvaluationBudget.Default;
        var reserved = SmtEvaluationThread.StackBytes(budget.MaxDepth);
        var fixture = SmtFixtures.All[0];
        var commands = SmtCheck.Commands(fixture.Problem);
        var model = SmtCheck.Model(fixture.Model, fixture.Values);
        var deep = Nested(budget.MaxDepth - 1);

        // Each reading is taken on the validation's own thread, after the evaluation and while
        // the thread still holds its stack: what the process has taken for the validation.
        var before = SmtMemoryReading.Now();
        var small = SmtEvaluationThread.Run(() => Evaluated(commands, model, null, budget), budget.MaxDepth);
        var between = SmtMemoryReading.Now();
        var ceiling = SmtEvaluationThread.Run(() => Evaluated([], SmtCheck.Model(null), deep, budget), budget.MaxDepth);
        var after = SmtMemoryReading.Now();

        TestContext.Out.WriteLine($"[memory] {RuntimeInformation.OSDescription.Trim()}, {RuntimeInformation.ProcessArchitecture}, .NET {Environment.Version}");
        TestContext.Out.WriteLine($"[memory] the stack a default validation reserves: {SmtMemoryReading.Megabytes(reserved)} MB for {budget.MaxDepth} levels");
        TestContext.Out.WriteLine($"[memory] before any validation: {before}");
        TestContext.Out.WriteLine($"[memory] in a validation of a small problem: {small.Reading} ({small.Reading?.Since(before)})");
        TestContext.Out.WriteLine($"[memory] after it, its thread gone: {between} ({between.Since(before)})");
        TestContext.Out.WriteLine($"[memory] in a validation at the ceiling: {ceiling.Reading} ({ceiling.Reading?.Since(between)})");
        TestContext.Out.WriteLine($"[memory] after it, its thread gone: {after} ({after.Since(between)})");

        Assert.That(small.Evaluated, Is.True, "the small problem was not evaluated");
        Assert.That(ceiling.Evaluated, Is.True, "the term at the ceiling was not evaluated");

        // Reserving the stack costs address space only: what the system charges the process
        // for stays far below the reservation.
        Assert.That(small.Reading!.Taken - before.Taken, Is.LessThan(reserved / 2), "a small validation took half of its reserved stack or more");
    }

    #endregion

    #region Tools

    /// <summary>
    /// Evaluates the assertions of a problem, or one term, on the thread it is called on, and
    /// reads the memory of the process there. Nothing is thrown on that thread: a test's
    /// assertion belongs on the test's own.
    /// </summary>
    private static (bool Evaluated, SmtMemoryReading? Reading) Evaluated(IReadOnlyList<SmtExpression> commands, SmtModel model, SmtExpression? term, SmtEvaluationBudget budget)
    {
        try
        {
            var problem = SmtEvaluationProblemReader.Read(commands);
            var run = new SmtRun(problem, model, budget);
            if (term != null)
                run.Evaluator.Eval(term, null, 1);

            foreach (var assertion in problem.Assertions)
                run.Evaluator.Eval(assertion, null, 1);

            return (true, SmtMemoryReading.Now());
        }
        catch (SmtPartialException)
        {
            return (false, null);
        }
    }

    /// <summary>
    /// A term of so many nested let, each the value of the one around it: the shape whose
    /// levels take the most stack.
    /// </summary>
    private static SmtExpression Nested(int levels)
    {
        var symbol = SmtExpression.Symbol;
        var term = symbol("true");
        for (var level = 0; level < levels; level++)
            term = SmtExpression.List(symbol("let"), SmtExpression.List(SmtExpression.List(symbol("x"), term)), symbol("x"));

        return term;
    }

    #endregion
}
