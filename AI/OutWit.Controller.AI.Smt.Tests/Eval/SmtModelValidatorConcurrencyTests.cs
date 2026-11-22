using System.Collections.Concurrent;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// One validator and one model, many threads: a validation keeps everything it makes to
/// itself and only reads the problem, so every thread gets the answer one thread gets, down to
/// the count of steps.
/// </summary>
[TestFixture]
public class SmtModelValidatorConcurrencyTests
{
    #region Constants

    private const int THREADS = 12;
    private const int ROUNDS = 6;

    #endregion

    #region Concurrency Tests

    [Test]
    public void OneValidatorGivesEveryThreadTheSameValidationTest()
    {
        // Each fixture with its model and every mutant of it: validated, failed and partial
        // answers are all in the mix.
        var cases = new List<(SmtModelValidator Validator, SmtModel Model, SmtValidation Expected)>();
        foreach (var fixture in SmtFixtures.All)
        {
            var validator = new SmtModelValidator(SmtCheck.Commands(fixture.Problem));
            var models = new List<SmtModel> { SmtCheck.Model(fixture.Model, fixture.Values), SmtCheck.Model(null), SmtCheck.Model("(") };
            models.AddRange(new SmtModelMutants(fixture).Make().Select(mutant => SmtCheck.Model(mutant.Model, mutant.Values)));
            foreach (var model in models)
                cases.Add((validator, model, validator.Validate(model, SmtEvaluationBudget.Default)));
        }

        var differences = new ConcurrentQueue<string>();
        var threads = Enumerable.Range(0, THREADS).Select(thread => new Thread(() =>
        {
            for (var round = 0; round < ROUNDS; round++)
            {
                // Every thread walks the cases from another place, so that the same validator
                // is in use by several of them at any moment.
                for (var index = 0; index < cases.Count; index++)
                {
                    var (validator, model, expected) = cases[(index + thread * 7 + round) % cases.Count];
                    var validation = validator.Validate(model, SmtEvaluationBudget.Default);
                    if (!validation.Is(expected))
                        differences.Enqueue($"{validation} instead of {expected}");
                }
            }
        })).ToList();

        threads.ForEach(thread => thread.Start());
        threads.ForEach(thread => thread.Join());

        Assert.That(differences, Is.Empty);
        Assert.That(cases.Select(item => item.Expected.Class).Distinct().Count(), Is.EqualTo(3));
    }

    [Test]
    public void BudgetThatEndsAnEvaluationEndsItAtTheSameStepOnEveryThreadTest()
    {
        const string problem = "(define-fun-rec count ((n Int)) Int (ite (<= n 0) 0 (+ 1 (count (- n 1)))))(assert (= (count 3000) 3000))";
        var validator = new SmtModelValidator(SmtCheck.Commands(problem));
        var budget = new SmtEvaluationBudget { MaxSteps = 12_345 };
        var expected = validator.Validate(SmtCheck.Model(null), budget);
        var results = new ConcurrentQueue<SmtValidation>();

        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = THREADS }, _ => results.Enqueue(validator.Validate(SmtCheck.Model(null), budget)));

        Assert.That(expected.Reason, Is.EqualTo(SmtValidationReasons.BUDGET_STEPS));
        Assert.That(expected.Steps, Is.EqualTo(budget.MaxSteps));
        Assert.That(results.All(result => result.Is(expected)), Is.True);
    }

    #endregion
}
