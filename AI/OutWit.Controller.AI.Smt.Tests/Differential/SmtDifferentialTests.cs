using System.Diagnostics;
using System.Globalization;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Controller.AI.Smt.Tests.Solver;

namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// The model evaluator against the solver itself, by volume: closed terms generated from a fixed
/// seed for each theory (<see cref="SmtDifferentialTheory"/>), each evaluated by the evaluator
/// and checked by the solver (<see cref="SmtDifferential"/>). Wherever the evaluator gives a
/// value, the solver must agree that the term equals it; where it gives none, its reason is
/// counted and the solver is not asked. The evaluator is open code that a dishonest node can
/// study, so its agreement with the solver is shown on terms nobody chose by hand.
/// <para>
/// The number of terms of each theory is <see cref="COUNT_VARIABLE"/> when it is set (the build
/// server runs a few thousand in all), <see cref="LOCAL_TERMS"/> otherwise. The totals of each
/// theory are written to the log: decided and agreed, partial by reason, disagreements. Needs
/// the solver module; without it the tests are ignored, with the reason, and on the build
/// server they fail.
/// </para>
/// </summary>
[TestFixture]
[Category("Differential")]
[NonParallelizable]
public sealed class SmtDifferentialTests
{
    #region Constants

    /// <summary>The variable that sets the number of terms of each theory.</summary>
    public const string COUNT_VARIABLE = "SMT_DIFFERENTIAL_TERMS";

    /// <summary>The number of terms of each theory when the variable is not set.</summary>
    public const int LOCAL_TERMS = 40;

    private const int SEED = 20261123;

    /// <summary>
    /// The least share of a theory's terms, in percent, that the evaluator gives a value for:
    /// a generator that drifts into terms the evaluator leaves open tests nothing, and shows here.
    /// </summary>
    private const int LEAST_DECIDED_PERCENT = 50;

    /// <summary>The least share of the functions' terms, in percent, that the evaluator decides on a value of the sort whose elements the model does not list.</summary>
    private const int LEAST_UNLISTED_PERCENT = 20;

    /// <summary>The names of the functions' prelude whose values or arguments are of that sort.</summary>
    private static readonly string[] UNLISTED_NAMES = ["v", "r", "s", "t"];

    #endregion

    #region Fields

    private List<SmtDifferentialOutcome> m_outcomes = null!;
    private string m_report = "";

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        SmtSolverModule.Require();
        var compile = SmtSolverRig.Warm();
        Assert.That(SmtSolverRig.Unavailable(), Is.Null, "this process has a sandbox for the solver");

        var count = Environment.GetEnvironmentVariable(COUNT_VARIABLE) is { Length: > 0 } text ? int.Parse(text, CultureInfo.InvariantCulture) : LOCAL_TERMS;
        var terms = new List<SmtGeneratedTerm>();
        foreach (var theory in Enum.GetValues<SmtDifferentialTheory>())
        {
            // A generator of its own for each theory: its terms do not depend on how many the others have.
            var generator = new SmtTermGenerator(SEED + (int)theory * 7_919);
            for (var number = 0; number < count; number++)
                terms.Add(generator.Next(theory, number));
        }

        var outcomes = new SmtDifferentialOutcome[terms.Count];
        var watch = Stopwatch.StartNew();
        Parallel.For(0, terms.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 4) }, index =>
        {
            outcomes[index] = SmtDifferential.Run(terms[index]);
        });

        m_outcomes = outcomes.ToList();
        m_report = Report(count, compile, watch.ElapsedMilliseconds);
        TestContext.Progress.WriteLine(m_report);
    }

    #endregion

    #region Differential Tests

    [Test]
    public void TheSolverAgreesWithEveryValueTheEvaluatorGivesTest()
    {
        TestContext.Out.WriteLine(m_report);
        Assert.Multiple(() =>
        {
            foreach (var outcome in m_outcomes.Where(outcome => outcome.Verdict == SmtDifferentialVerdict.Disagreed))
                Assert.Fail($"a disagreement: {outcome}");
        });
    }

    [Test]
    public void TheSolverAnswersForEveryTermTheEvaluatorDecidesTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var outcome in m_outcomes.Where(outcome => outcome.Verdict == SmtDifferentialVerdict.NotTaken))
                Assert.Fail($"the solver gave no answer: {outcome}");
        });
    }

    [Test]
    public void TheEvaluatorNeverFailsOnAGeneratedTermTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var outcome in m_outcomes.Where(outcome => outcome.Verdict == SmtDifferentialVerdict.EvaluatorFault))
                Assert.Fail($"a fault of the evaluator: {outcome}");
        });
    }

    [Test]
    public void TheSolverDoesNotKnowTheDivisibilityTheEvaluatorComputesTest()
    {
        // The standard's (_ divisible k): the evaluator computes it, the solver calls it unknown
        // under every logic, so a problem that uses it never comes back sat, and the generated
        // terms write it out as (= (mod t k) 0).
        var answer = SmtRawSolver.Ask("(set-logic QF_LIA)(declare-const x Int)(assert ((_ divisible 3) x))");

        Assert.That(answer, Does.Contain("unknown constant divisible"));
        Assert.That(SmtCheck.Evaluate("((_ divisible 3) 9)"), Is.SameAs(SmtBoolValue.TRUE));
    }

    [Test]
    public void TheEvaluatorDecidesMostTermsOfEveryTheoryTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var group in m_outcomes.GroupBy(outcome => outcome.Term.Theory))
            {
                var decided = group.Count(outcome => outcome.Verdict != SmtDifferentialVerdict.Partial);
                Assert.That(decided * 100, Is.GreaterThanOrEqualTo(group.Count() * LEAST_DECIDED_PERCENT), $"{group.Key}: {decided} of {group.Count()} decided");
            }
        });
    }

    [Test]
    public void TheFunctionsReachTheElementsTheModelDoesNotListTest()
    {
        // The sort V of the functions' prelude: its elements are named by the model, never listed.
        var functions = m_outcomes.Where(outcome => outcome.Term.Theory == SmtDifferentialTheory.Functions).ToList();
        var unlisted = functions.Count(outcome => outcome.Verdict == SmtDifferentialVerdict.Agreed && Names(outcome.Term.Term).Overlaps(UNLISTED_NAMES));

        TestContext.Out.WriteLine($"{unlisted} of {functions.Count} terms of the functions decided and agreed on with an element the model does not list");
        Assert.That(unlisted * 100, Is.GreaterThanOrEqualTo(functions.Count * LEAST_UNLISTED_PERCENT));
    }

    #endregion

    #region Tools

    /// <summary>The symbols a term holds.</summary>
    private static HashSet<string> Names(string term)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<SmtExpression>(SmtReader.Read(term, SmtReaderLimits.Problem));
        while (pending.Count > 0)
        {
            var expression = pending.Pop();
            if (expression.Kind == SmtExpressionKind.Symbol)
                names.Add(expression.Text);

            foreach (var item in expression.Items)
                pending.Push(item);
        }

        return names;
    }

    private string Report(int count, long compileMs, long elapsedMs)
    {
        var lines = new List<string>
        {
            $"the evaluator against the solver: {m_outcomes.Count} terms, {count} of each theory, seed {SEED}; the module compiled in {compileMs} ms; both ran in {elapsedMs} ms",
            $"  {"theory",-14} {"terms",6} {"decided",8} {"agreed",7} {"disagreed",10} {"no answer",10} {"faults",7}  partial by reason"
        };

        foreach (var group in m_outcomes.GroupBy(outcome => outcome.Term.Theory).OrderBy(group => group.Key))
        {
            var partial = group.Where(outcome => outcome.Verdict == SmtDifferentialVerdict.Partial)
                .GroupBy(outcome => outcome.Reason!, StringComparer.Ordinal)
                .OrderBy(reason => reason.Key, StringComparer.Ordinal)
                .Select(reason => $"{reason.Key} {reason.Count()}");
            lines.Add($"  {group.Key,-14} {group.Count(),6} {group.Count(outcome => outcome.Verdict != SmtDifferentialVerdict.Partial),8} "
                      + $"{group.Count(outcome => outcome.Verdict == SmtDifferentialVerdict.Agreed),7} {group.Count(outcome => outcome.Verdict == SmtDifferentialVerdict.Disagreed),10} "
                      + $"{group.Count(outcome => outcome.Verdict == SmtDifferentialVerdict.NotTaken),10} {group.Count(outcome => outcome.Verdict == SmtDifferentialVerdict.EvaluatorFault),7}  "
                      + string.Join(", ", partial));
        }

        foreach (var outcome in m_outcomes.Where(outcome => outcome.Verdict is not (SmtDifferentialVerdict.Agreed or SmtDifferentialVerdict.Partial)))
            lines.Add($"    {outcome.Verdict}: {outcome}");

        return string.Join(Environment.NewLine, lines);
    }

    #endregion
}
