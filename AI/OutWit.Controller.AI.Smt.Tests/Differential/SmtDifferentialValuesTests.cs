using System.Diagnostics;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// The check of reported values against the solver itself, by volume: the terms of
/// <see cref="SmtDifferentialTests"/> (<see cref="SmtDifferentialTerms"/>), each asked of the
/// solver with <c>get-value</c> as a row asks, and the solver's own answer held to the term and
/// to the solver's own model as the host holds a row (<see cref="SmtDifferentialValues"/>).
/// <para>
/// What must hold of every honest answer: its pairs read as the terms asked; it is never failed
/// (a solver and the evaluator may spell one value two ways, and no spelling may accuse a
/// node); it is of the class its model alone is of wherever the evaluator computes the value,
/// and partial with the evaluator's reason where it does not. And of every answer with a value
/// changed into another: it is never validated.
/// </para>
/// <para>
/// The number of terms of each theory is <see cref="SmtDifferentialTerms.COUNT_VARIABLE"/> when
/// it is set, <see cref="SmtDifferentialTerms.LOCAL_TERMS"/> otherwise. Needs the solver module;
/// without it the tests are ignored, with the reason, and on the build server they fail.
/// </para>
/// </summary>
[TestFixture]
[Category("Differential")]
[NonParallelizable]
public sealed class SmtDifferentialValuesTests
{
    #region Constants

    /// <summary>The least share of a theory's answers, in percent, whose values the evaluator decides: a check that leaves most values open checks nothing.</summary>
    private const int LEAST_VALIDATED_PERCENT = 50;

    /// <summary>The least share, in percent, of the changed values of validated answers that are found to be another value and not merely left open.</summary>
    private const int LEAST_CAUGHT_PERCENT = 90;

    /// <summary>The reasons a value the solver gives may be left open for: what the evaluator does not compute, or does not compare.</summary>
    private static readonly string[] OPEN =
    [
        SmtValidationReasons.DIVISION_BY_ZERO, SmtValidationReasons.SELECTOR_OF_OTHER_CONSTRUCTOR, SmtValidationReasons.STRING_LITERAL,
        SmtValidationReasons.REGULAR_EXPRESSION, SmtValidationReasons.ARRAY_COMPARISON, SmtValidationReasons.UNSUPPORTED_OPERATOR,
        SmtValidationReasons.UNSUPPORTED_SORT, SmtValidationReasons.ALGEBRAIC_NUMBER, SmtValidationReasons.QUANTIFIER,
        SmtValidationReasons.FLOATING_POINT
    ];

    #endregion

    #region Fields

    private List<SmtDifferentialValuesOutcome> m_outcomes = null!;
    private string m_report = "";

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        SmtSolverModule.Require();
        var compile = SmtSolverRig.Warm();
        Assert.That(SmtSolverRig.Unavailable(), Is.Null, "this process has a sandbox for the solver");

        var count = SmtDifferentialTerms.Count();
        var terms = SmtDifferentialTerms.Generate(count);

        var outcomes = new SmtDifferentialValuesOutcome[terms.Count];
        var watch = Stopwatch.StartNew();
        Parallel.For(0, terms.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 4) }, index =>
        {
            outcomes[index] = SmtDifferentialValues.Run(terms[index]);
        });

        m_outcomes = outcomes.ToList();
        m_report = Report(count, compile, watch.ElapsedMilliseconds);
        TestContext.Progress.WriteLine(m_report);
    }

    #endregion

    #region Honest Answer Tests

    [Test]
    public void TheSolverAnswersForEveryTermTest()
    {
        TestContext.Out.WriteLine(m_report);
        Assert.Multiple(() =>
        {
            foreach (var outcome in m_outcomes.Where(outcome => !outcome.Answered))
                Assert.Fail($"no answer: {outcome}");
        });
    }

    [Test]
    public void TheSolverEchoesEveryGeneratedTermAsItWasWrittenTest()
    {
        // What lets the validator refuse a pair of any other term: an honest answer always reads as the terms asked.
        Assert.Multiple(() =>
        {
            foreach (var outcome in Answered().Where(outcome => !outcome.Echoed))
                Assert.Fail($"a pair that does not read as the term asked: {outcome}");
        });
    }

    [Test]
    public void NoAnswerOfTheSolverIsFailedTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var outcome in Answered().Where(outcome => outcome.Class == SmtValidationClass.Failed))
                Assert.Fail($"the solver's own answer is called wrong: {outcome}");

            foreach (var outcome in Answered().Where(outcome => outcome.Reason is SmtValidationReasons.UNREADABLE_VALUES or SmtValidationReasons.EVALUATOR_ERROR))
                Assert.Fail($"the solver's own answer is not taken: {outcome}");
        });
    }

    [Test]
    public void AnswerIsOfTheClassOfItsModelWhereverItsValuesAreComputedTest()
    {
        // The values only ever take a validated answer to partial, and only for a reason that
        // says the evaluator does not compute or does not compare a value.
        Assert.Multiple(() =>
        {
            foreach (var outcome in Answered())
            {
                if (outcome.Alone != SmtValidationClass.Validated)
                    Assert.That((outcome.Class, outcome.Reason), Is.EqualTo((outcome.Alone, outcome.AloneReason)), outcome.ToString());
                else if (outcome.Class != SmtValidationClass.Validated)
                    Assert.That(OPEN, Does.Contain(outcome.Reason), outcome.ToString());
            }
        });
    }

    [Test]
    public void TheEvaluatorDecidesMostValuesOfEveryTheoryTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var group in Answered().GroupBy(outcome => outcome.Term.Theory))
            {
                var validated = group.Count(outcome => outcome.Class == SmtValidationClass.Validated);
                Assert.That(validated * 100, Is.GreaterThanOrEqualTo(group.Count() * LEAST_VALIDATED_PERCENT), $"{group.Key}: {validated} of {group.Count()} validated");
            }
        });
    }

    #endregion

    #region Changed Value Tests

    [Test]
    public void AnswerWithAValueChangedIsNeverValidatedTest()
    {
        var changed = Answered().Where(outcome => outcome.Changed).ToList();

        Assert.That(changed, Has.Count.GreaterThan(m_outcomes.Count / 2), "most values have a spelling that can be changed");
        Assert.Multiple(() =>
        {
            foreach (var outcome in changed.Where(outcome => outcome.ChangedClass == SmtValidationClass.Validated))
                Assert.Fail($"another value was taken for the term's: {outcome}");
        });
    }

    [Test]
    public void AChangedValueOfAValidatedAnswerIsFoundToBeAnotherTest()
    {
        // Where the evaluator decided the solver's value, it decides the changed one too: failed, by the value.
        var decided = Answered().Where(outcome => outcome.Changed && outcome.Class == SmtValidationClass.Validated).ToList();
        var caught = decided.Count(outcome => outcome.ChangedClass == SmtValidationClass.Failed && outcome.ChangedReason == SmtValidationReasons.VALUE_MISMATCH);

        TestContext.Out.WriteLine($"{caught} of {decided.Count} changed values of validated answers failed by the value");
        foreach (var outcome in decided.Where(outcome => outcome.ChangedClass != SmtValidationClass.Failed))
            TestContext.Out.WriteLine($"  left open: {outcome}");

        Assert.That(caught * 100, Is.GreaterThanOrEqualTo(decided.Count * LEAST_CAUGHT_PERCENT));
    }

    #endregion

    #region Tools

    private IEnumerable<SmtDifferentialValuesOutcome> Answered()
    {
        return m_outcomes.Where(outcome => outcome.Answered);
    }

    private string Report(int count, long compileMs, long elapsedMs)
    {
        var lines = new List<string>
        {
            $"the values of the solver against the evaluator: {m_outcomes.Count} terms, {count} of each theory, seed {SmtDifferentialTerms.SEED}; the module compiled in {compileMs} ms; both ran in {elapsedMs} ms",
            $"  {"theory",-14} {"terms",6} {"answered",9} {"echoed",7} {"validated",10} {"failed",7} {"changed",8} {"caught",7}  partial by reason"
        };

        foreach (var group in m_outcomes.GroupBy(outcome => outcome.Term.Theory).OrderBy(group => group.Key))
        {
            var answered = group.Where(outcome => outcome.Answered).ToList();
            var partial = answered.Where(outcome => outcome.Class == SmtValidationClass.Partial)
                .GroupBy(outcome => outcome.Reason ?? "", StringComparer.Ordinal)
                .OrderBy(reason => reason.Key, StringComparer.Ordinal)
                .Select(reason => $"{reason.Key} {reason.Count()}");
            lines.Add($"  {group.Key,-14} {group.Count(),6} {answered.Count,9} {answered.Count(outcome => outcome.Echoed),7} "
                      + $"{answered.Count(outcome => outcome.Class == SmtValidationClass.Validated),10} {answered.Count(outcome => outcome.Class == SmtValidationClass.Failed),7} "
                      + $"{answered.Count(outcome => outcome.Changed),8} {answered.Count(outcome => outcome.Changed && outcome.ChangedClass == SmtValidationClass.Failed),7}  "
                      + string.Join(", ", partial));
        }

        foreach (var outcome in m_outcomes.Where(outcome => !outcome.Answered || !outcome.Echoed || outcome.Class == SmtValidationClass.Failed || outcome.Changed && outcome.ChangedClass == SmtValidationClass.Validated))
            lines.Add($"    {outcome}");

        return string.Join(Environment.NewLine, lines);
    }

    #endregion
}
