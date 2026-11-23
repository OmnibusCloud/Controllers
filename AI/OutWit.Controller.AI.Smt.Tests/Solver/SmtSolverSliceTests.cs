using System.Diagnostics;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// The solver on real problems, without a byte of them in the repository: the 469 problems of
/// the SMT-LIB release the committed list names (<see cref="SmtSlice"/>), each solved as a
/// <c>check</c> in a session of its own, under an envelope none of them comes near: a resource
/// limit of 2,000,000,000, a hundred seconds of the reference rate, 1,024 MB.
/// <para>
/// Three things are held, each with its numbers written here. The solver's verdict is the
/// <c>:status</c> the problem's authors gave it, for every one. The model of every satisfiable
/// one goes to the model evaluator, which shares no code with the solver: the classes are
/// counted by logic, and none is <c>failed</c>. And each model the evaluator took a side on is
/// written back into its problem (the closed script) and handed to the solver again, which then
/// has nothing left to choose: it must say <c>sat</c> where the evaluator said validated and
/// <c>unsat</c> where it said failed. A contradiction names the problem.
/// </para>
/// <para>
/// Needs the solver module and the fetched slice; without either the tests are ignored, with
/// the reason, and on the build server they fail.
/// </para>
/// </summary>
[TestFixture]
[Category("Slice")]
[NonParallelizable]
public sealed class SmtSolverSliceTests
{
    #region Constants

    private const long RESOURCE_LIMIT = 2_000_000_000;
    private const int HARD_CAP_SECONDS = 100;
    private const int MEMORY_MEGABYTES = 1_024;

    private const int SAT = 225;
    private const int UNSAT = 244;

    /// <summary>The logics the evaluator's coverage is stated for.</summary>
    private static readonly string[] COVERED = ["QF_LIA", "QF_LRA", "QF_BV", "QF_UF", "QF_AX", "QF_ALIA", "QF_UFLIA", "QF_DT", "QF_UFDT"];

    private const int COVERED_SAT = 215;
    private const int COVERED_VALIDATED_AT_LEAST = 212;

    /// <summary>
    /// The evaluator's classes of the satisfiable problems' models, by logic: validated, partial,
    /// failed. A rule of the evaluator that starts or stops closing real models shows here.
    /// </summary>
    private static readonly Dictionary<string, (int Validated, int Partial, int Failed)> CLASSES = new(StringComparer.Ordinal)
    {
        ["QF_LIA"] = (75, 0, 0),
        ["QF_BV"] = (74, 1, 0),
        ["QF_LRA"] = (20, 0, 0),
        ["QF_UF"] = (10, 0, 0),
        ["QF_AX"] = (10, 0, 0),
        ["QF_ALIA"] = (6, 0, 0),
        ["QF_UFLIA"] = (10, 0, 0),
        ["QF_DT"] = (4, 2, 0),
        ["QF_UFDT"] = (3, 0, 0),
        ["QF_S"] = (5, 5, 0)
    };

    /// <summary>
    /// The models the evaluator took a side on, each of which the solver is handed again as a
    /// closed script, takes, and agrees with the evaluator on: all of them.
    /// </summary>
    private const int CLOSED = 217;

    /// <summary>
    /// The closed scripts that declare elements the model names without listing them, by
    /// problem: the model gives a function a value that is an element of a sort
    /// (<c>A$!val!0</c>) whose universe it does not list. Written here by name so that a script
    /// that starts or stops needing such a declaration shows.
    /// </summary>
    private static readonly string[] CLOSED_WITH_UNLISTED = ["p481", "p482"];

    #endregion

    #region Fields

    private List<SmtSliceResult> m_results = null!;
    private Dictionary<string, int> m_unlisted = null!;
    private int m_listed;
    private string m_report = "";

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        SmtSolverModule.Require();
        var folder = SmtSlice.RequireFolder();
        var rows = SmtSlice.Listing();
        var options = new SmtOptionsData { ResourceLimit = RESOURCE_LIMIT, HardCapSeconds = HARD_CAP_SECONDS, MemoryMegabytes = MEMORY_MEGABYTES };

        var compile = SmtSolverRig.Warm();
        Assert.That(SmtSolverRig.Unavailable(), Is.Null, "this process has a sandbox for the solver");

        m_listed = rows.Count;
        m_results = [];
        m_unlisted = new Dictionary<string, int>(StringComparer.Ordinal);
        var solving = new Stopwatch();
        var validating = new Stopwatch();
        var closing = new Stopwatch();

        foreach (var row in rows)
        {
            var text = SmtSlice.Read(folder, row.Id, row.Sha256);
            if (text == null)
            {
                if (SmtSolverModule.OnBuildServer)
                    Assert.Fail($"{row.Id} is not in {folder}: the slice was not fetched whole");

                continue;
            }

            var problem = SmtSanitizer.Sanitize(text).Problem;
            Assert.That(problem, Is.Not.Null, $"{row.Id} is refused by the sanitizer");

            solving.Start();
            var outcome = SmtSolverRig.Solve(new SmtProblemData { Id = row.Id, Query = SmtQuery.Check, Text = problem!.Canonical, Hash = problem.Hash, Expected = row.Status }, options);
            solving.Stop();

            SmtValidation? validation = null;
            SmtSolved? closed = null;
            string? closedRefusal = null;
            var free = 0;
            if (outcome.Verdict == "sat")
            {
                validating.Start();
                var model = SmtModel.Read(outcome.Models.FirstOrDefault(), null, SmtReaderLimits.Unbounded);
                validation = new SmtModelValidator(problem).Validate(model, SmtEvaluationBudget.Default);
                validating.Stop();

                if (validation.Class != SmtValidationClass.Partial)
                {
                    closing.Start();
                    var script = SmtClosedScript.Build(problem.Commands, model);
                    free = script.Free;
                    m_unlisted[row.Id] = script.Unlisted;
                    var again = SmtSanitizer.Sanitize(script.ToString());
                    if (again.Problem == null)
                        closedRefusal = $"{again.Refusal!.Code}, line {again.Refusal.Line}: {again.Refusal.Reason}";
                    else
                        closed = SmtSolverRig.Solve(new SmtProblemData { Id = row.Id, Query = SmtQuery.Check, Text = again.Problem.Canonical, Hash = again.Problem.Hash }, options);

                    closing.Stop();
                }
            }

            m_results.Add(new SmtSliceResult
            {
                Id = row.Id, Logic = row.Logic, Expected = row.Status, Outcome = outcome,
                Validation = validation, ClosedFree = free, ClosedRefusal = closedRefusal, Closed = closed
            });
        }

        m_report = Report(compile, solving.ElapsedMilliseconds, validating.ElapsedMilliseconds, closing.ElapsedMilliseconds);
        TestContext.Progress.WriteLine(m_report);
    }

    #endregion

    #region Slice Tests

    [Test]
    public void EveryVerdictIsTheStatusTheProblemsAuthorsGaveItTest()
    {
        TestContext.Out.WriteLine(m_report);
        Assert.Multiple(() =>
        {
            foreach (var result in m_results)
            {
                Assert.That(result.Outcome.Verdict, Is.EqualTo(result.Expected == SmtProblemStatus.Sat ? "sat" : "unsat"), $"{result.Id} ({result.Logic}): {result.Outcome}");
                Assert.That(result.Outcome.Status, Is.EqualTo("ok").Or.EqualTo("output_exceeded"), $"{result.Id} ({result.Logic}): {result.Outcome}");
                Assert.That(result.Outcome.Checks, Is.EqualTo(1), result.Id);
            }

            if (Whole())
            {
                Assert.That(m_results, Has.Count.EqualTo(SmtSlice.PROBLEMS));
                Assert.That(m_results.Count(result => result.Outcome.Verdict == "sat"), Is.EqualTo(SAT));
                Assert.That(m_results.Count(result => result.Outcome.Verdict == "unsat"), Is.EqualTo(UNSAT));
            }
        });
    }

    [Test]
    public void EveryModelIsClassifiedByTheEvaluatorAsWrittenHereAndNoneFailsTest()
    {
        var classes = m_results
            .Where(result => result.Validation != null)
            .GroupBy(result => result.Logic, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (
                Validated: group.Count(result => result.Validation!.Class == SmtValidationClass.Validated),
                Partial: group.Count(result => result.Validation!.Class == SmtValidationClass.Partial),
                Failed: group.Count(result => result.Validation!.Class == SmtValidationClass.Failed)), StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            foreach (var result in m_results.Where(result => result.Validation?.Class == SmtValidationClass.Failed))
                Assert.Fail($"{result.Id} ({result.Logic}): the evaluator says the solver's model does not satisfy the problem: {result.Validation!.Reason} {result.Validation.Detail}");

            foreach (var result in m_results.Where(result => result.Validation?.Reason == SmtValidationReasons.EVALUATOR_ERROR))
                Assert.Fail($"{result.Id} ({result.Logic}): a defect of the evaluator: {result.Validation!.Detail}");

            if (!Whole())
                return;

            Assert.That(classes, Is.EquivalentTo(CLASSES), "validated, partial and failed, by logic");

            var covered = classes.Where(pair => COVERED.Contains(pair.Key)).ToList();
            Assert.That(covered.Sum(pair => pair.Value.Validated + pair.Value.Partial + pair.Value.Failed), Is.EqualTo(COVERED_SAT), "the satisfiable problems of the nine logics the evaluator's coverage is stated for");
            Assert.That(covered.Sum(pair => pair.Value.Validated), Is.GreaterThanOrEqualTo(COVERED_VALIDATED_AT_LEAST), $"of {COVERED_SAT}");
        });
    }

    [Test]
    public void TheSolverAgreesWithTheEvaluatorOnEveryModelWrittenBackIntoItsProblemTest()
    {
        var sided = m_results.Where(result => result.Validation != null && result.Validation.Class != SmtValidationClass.Partial).ToList();
        var agreed = 0;

        Assert.Multiple(() =>
        {
            foreach (var result in sided)
            {
                Assert.That(result.ClosedRefusal, Is.Null, $"{result.Id} ({result.Logic}): the closed script is refused by the sanitizer");
                if (result.Closed == null)
                    continue;

                var expected = result.Validation!.Class == SmtValidationClass.Validated ? "ok sat" : "ok unsat";
                Assert.That(result.Closed.Words(), Is.EqualTo(expected),
                    $"{result.Id} ({result.Logic}): the evaluator says {result.Validation.Class} and the solver, given the model as definitions, says otherwise: {result.Closed}");
                if (result.Closed.Words() == expected)
                    agreed++;
            }

            if (!Whole())
                return;

            Assert.That(sided.Count(result => result.Closed != null), Is.EqualTo(CLOSED), "the closed scripts the solver was handed");
            Assert.That(agreed, Is.EqualTo(CLOSED), "the closed scripts on which the solver and the evaluator agree");
            Assert.That(m_unlisted.Where(pair => pair.Value > 0).Select(pair => pair.Key).Order(StringComparer.Ordinal), Is.EqualTo(CLOSED_WITH_UNLISTED), "the closed scripts that declare elements the model does not list");
        });
    }

    #endregion

    #region Tools

    /// <summary>Whether every problem of the list was there: the pinned numbers are about the whole slice.</summary>
    private bool Whole()
    {
        return m_results.Count == m_listed;
    }

    private string Report(long compileMs, long solveMs, long validateMs, long closeMs)
    {
        var lines = new List<string>
        {
            $"the slice: {m_results.Count} of {m_listed} problems solved; the module compiled in {compileMs} ms",
            $"  verdicts: {m_results.Count(result => result.Outcome.Verdict == "sat")} sat, {m_results.Count(result => result.Outcome.Verdict == "unsat")} unsat, "
            + $"{m_results.Count(result => result.Outcome.Verdict is not ("sat" or "unsat"))} other; statuses: "
            + string.Join(", ", m_results.GroupBy(result => result.Outcome.Status).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Count()} {group.Key}"))
        };

        var fuel = m_results.Sum(result => result.Outcome.Fuel);
        var wall = m_results.Sum(result => (long)result.Outcome.WallMs);
        var slowest = m_results.OrderByDescending(result => result.Outcome.WallMs).Take(3).Select(result => $"{result.Id} {result.Outcome.WallMs} ms").ToList();
        lines.Add($"  the checks: {solveMs} ms on this machine ({wall} ms inside the sessions), fuel {fuel:N0}, {(wall == 0 ? 0 : fuel / wall * 1000):N0} units a second; the slowest: {string.Join(", ", slowest)}");
        lines.Add($"  the most fuel of one problem: {m_results.Max(result => result.Outcome.Fuel):N0}; the most memory: {m_results.Max(result => result.Outcome.PeakMemoryBytes):N0} bytes; "
                  + $"the largest model: {m_results.Max(result => result.Outcome.Models.Sum(model => (long)model.Length)):N0} characters; "
                  + $"models above 64 KB: {m_results.Count(result => result.Outcome.Models.Sum(model => (long)model.Length) > 64 * 1024)}");

        lines.Add($"  the evaluator: {validateMs} ms for {m_results.Count(result => result.Validation != null)} models");
        foreach (var group in m_results.Where(result => result.Validation != null).GroupBy(result => result.Logic, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var partial = group.Where(result => result.Validation!.Class == SmtValidationClass.Partial).Select(result => $"{result.Id} {result.Validation!.Reason}").ToList();
            lines.Add($"    {group.Key,-9} validated {group.Count(result => result.Validation!.Class == SmtValidationClass.Validated),3}  partial {partial.Count,2}  "
                      + $"failed {group.Count(result => result.Validation!.Class == SmtValidationClass.Failed),2}{(partial.Count == 0 ? "" : $"  ({string.Join(", ", partial)})")}");
        }

        var closed = m_results.Where(result => result.Closed != null).ToList();
        lines.Add($"  the closed scripts: {closed.Count} checked by the solver in {closeMs} ms: "
                  + $"{closed.Count(result => result.Closed!.Verdict == "sat")} sat, {closed.Count(result => result.Closed!.Verdict == "unsat")} unsat, "
                  + $"{closed.Count(result => result.Closed!.Verdict is not ("sat" or "unsat"))} other; "
                  + $"{m_results.Count(result => result.ClosedRefusal != null)} refused by the sanitizer; {closed.Count(result => result.ClosedFree > 0)} with symbols the model left out; "
                  + $"{m_unlisted.Count(pair => pair.Value > 0)} with elements the model does not list");
        foreach (var result in closed.Where(result => result.Closed!.Status != "ok"))
            lines.Add($"    {result.Id} ({result.Logic}): {result.Closed!.Status}: {result.Closed.Message}");

        return string.Join(Environment.NewLine, lines);
    }

    #endregion
}
