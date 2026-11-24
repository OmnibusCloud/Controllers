using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Kit;

/// <summary>
/// The options of the long-tail kit (<c>AI/OutWit.Controller.AI.Smt.Live/demo/03-long-tail</c>)
/// against the solver itself: the problems its list marks as the sample (ten of each logic and
/// status, spread over the times Z3 took at the competitions, from 1 to 60 seconds), each solved
/// as a <c>check</c> in a session of its own under the options the kit's <c>SCRIPT.md</c> gives a
/// run. Fuel is a count, so what a problem comes to is the same on every machine and in every
/// run, and the numbers written here hold everywhere.
/// <para>
/// Held: every verdict is the problem's <c>:status</c>, or unknown because a budget ended; no
/// model the evaluator checks fails; and the options decide the share of the sample written
/// here. The log states each problem's cost against its competition time: the fuel as seconds
/// of the reference machine, the wall time on this machine, the peak of the solver's memory.
/// </para>
/// <para>
/// The problems are not in the repository: the kit's <c>make_set.py</c> fetches them and copies
/// the sample into its own folder, which <see cref="FOLDER_VARIABLE"/> names. Without it the tests
/// are ignored, with the reason, everywhere: only the build server's job of the kit names it, and
/// there a sample file that is missing is a failure.
/// </para>
/// </summary>
[TestFixture]
[Category("LongTail")]
[NonParallelizable]
public sealed class SmtLongTailSampleTests
{
    #region Constants

    /// <summary>Names the folder that holds the sample's problems as <c>&lt;id&gt;.smt2</c>.</summary>
    public const string FOLDER_VARIABLE = "WITAI_SMT_LONG_TAIL_DIR";

    private const string LISTING = "Kit/kit-list.tsv";
    private const string EXTENSION = ".smt2";
    private const char SEPARATOR = '\t';

    // The options of the kit's SCRIPT.md.
    private const long RESOURCE_LIMIT = 4_294_967_295;
    private const int HARD_CAP_SECONDS = 300;
    private const int MEMORY_MEGABYTES = 1_024;

    private const int SAMPLE = 40;
    private const int MAX_DEGREE = 4;
    private const double FUEL_PER_SECOND = 5_000_000_000;

    /// <summary>The unknowns that are a budget's end: the resource limit, the fuel of the hard cap, the memory.</summary>
    private static readonly string[] BUDGET_ENDS = ["rlimit", "fuel", "memout"];

    /// <summary>The problems of the sample the options leave undecided, by id: none.</summary>
    private static readonly string[] UNDECIDED = [];

    /// <summary>
    /// The fuel of the sample's forty sessions together: a count, the same on every machine for
    /// the pinned module and engine. The most of one problem is about a hundred seconds of the
    /// reference machine, a third of the hard cap.
    /// </summary>
    private const long SAMPLE_FUEL = 1_488_437_746_353;

    private static readonly UTF8Encoding STRICT = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    #endregion

    #region Fields

    private List<SmtLongTailResult> m_results = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var folder = Environment.GetEnvironmentVariable(FOLDER_VARIABLE);
        if (string.IsNullOrEmpty(folder))
            Assert.Ignore($"{FOLDER_VARIABLE} names no folder: the long-tail kit's make_set.py fetches the sample, and the kit's job on the build server names it");

        Assert.That(Directory.Exists(folder), Is.True, $"{folder} is not a folder");
        SmtSolverModule.Require();

        var rows = Listing().Where(row => row.Sample).ToList();
        Assert.That(rows, Has.Count.EqualTo(SAMPLE), "the sample the list marks");

        var compile = SmtSolverRig.Warm();
        Assert.That(SmtSolverRig.Unavailable(), Is.Null, "this process has a sandbox for the solver");

        var options = new SmtOptionsData { ResourceLimit = RESOURCE_LIMIT, HardCapSeconds = HARD_CAP_SECONDS, MemoryMegabytes = MEMORY_MEGABYTES };
        var results = new ConcurrentBag<SmtLongTailResult>();
        var degree = Math.Clamp(Environment.ProcessorCount, 1, MAX_DEGREE);
        var started = DateTime.UtcNow;

        Parallel.ForEach(rows, new ParallelOptions { MaxDegreeOfParallelism = degree }, row =>
        {
            var path = Path.Combine(folder!, row.Id + EXTENSION);
            if (!File.Exists(path))
            {
                results.Add(new SmtLongTailResult { Row = row, Fault = $"{path} is not there" });
                return;
            }

            var bytes = File.ReadAllBytes(path);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != row.Sha256)
            {
                results.Add(new SmtLongTailResult { Row = row, Fault = $"{path} is not the file the list names: its SHA-256 differs" });
                return;
            }

            var sanitized = SmtSanitizer.Sanitize(STRICT.GetString(bytes));
            if (sanitized.Problem == null)
            {
                results.Add(new SmtLongTailResult { Row = row, Fault = $"refused: {sanitized.Refusal!.Code}, line {sanitized.Refusal.Line}: {sanitized.Refusal.Reason}" });
                return;
            }

            var problem = sanitized.Problem;
            var outcome = SmtSolverRig.Solve(new SmtProblemData { Id = row.Id, Query = SmtQuery.Check, Text = problem.Canonical, Hash = problem.Hash, Expected = row.Status == "sat" ? SmtProblemStatus.Sat : SmtProblemStatus.Unsat }, options);

            SmtValidation? validation = null;
            if (outcome.Verdict == "sat" && outcome.Models.Count > 0)
                validation = new SmtModelValidator(problem).Validate(SmtModel.Read(outcome.Models[0], null, SmtReaderLimits.Unbounded), SmtEvaluationBudget.Default);

            results.Add(new SmtLongTailResult { Row = row, Outcome = outcome, Validation = validation });
        });

        m_results = results.OrderBy(result => result.Row.Id, StringComparer.Ordinal).ToList();
        TestContext.Progress.WriteLine(Report(compile, degree, DateTime.UtcNow - started));
    }

    #endregion

    #region Sample Tests

    [Test]
    public void EveryProblemOfTheSampleIsSolvedTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var result in m_results)
                Assert.That(result.Fault, Is.Null, result.Row.Id);

            Assert.That(m_results, Has.Count.EqualTo(SAMPLE));
        });
    }

    [Test]
    public void EveryVerdictIsTheStatusOrABudgetsEndTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var result in m_results.Where(result => result.Outcome != null))
            {
                var outcome = result.Outcome!;
                Assert.That(outcome.Status, Is.EqualTo("ok").Or.EqualTo("output_exceeded"), $"{result.Row.Id} ({result.Row.Logic}): {outcome}");
                if (outcome.Verdict == "unknown")
                    Assert.That(outcome.Reason, Is.AnyOf(BUDGET_ENDS), $"{result.Row.Id} ({result.Row.Logic}): unknown for a reason that is no budget's end: {outcome}");
                else
                    Assert.That(outcome.Verdict, Is.EqualTo(result.Row.Status), $"{result.Row.Id} ({result.Row.Logic}): {outcome}");
            }
        });
    }

    [Test]
    public void NoModelOfTheSampleFailsTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var result in m_results.Where(result => result.Validation != null))
            {
                Assert.That(result.Validation!.Class, Is.Not.EqualTo(SmtValidationClass.Failed),
                    $"{result.Row.Id} ({result.Row.Logic}): the evaluator says the solver's model does not satisfy the problem: {result.Validation.Reason} {result.Validation.Detail}");
                Assert.That(result.Validation.Reason, Is.Not.EqualTo(SmtValidationReasons.EVALUATOR_ERROR), $"{result.Row.Id} ({result.Row.Logic}): a defect of the evaluator: {result.Validation.Detail}");
            }
        });
    }

    [Test]
    public void TheOptionsLeaveUndecidedTheProblemsWrittenHereTest()
    {
        var undecided = m_results.Where(result => result.Outcome?.Verdict == "unknown").Select(result => result.Row.Id).Order(StringComparer.Ordinal);

        Assert.That(undecided, Is.EqualTo(UNDECIDED), "a problem of the sample that a budget ended");
    }

    [Test]
    public void TheSampleBurnsTheFuelWrittenHereTest()
    {
        Assert.That(m_results.Sum(result => result.Outcome?.Fuel ?? 0), Is.EqualTo(SAMPLE_FUEL), "the fuel of the sample's sessions together");
    }

    #endregion

    #region Tools

    private string Report(long compile, int degree, TimeSpan elapsed)
    {
        var text = new StringBuilder();
        text.AppendLine($"long-tail sample: {m_results.Count} problems, {degree} at once, {elapsed.TotalSeconds:F0} s; module compile {compile} ms");
        text.AppendLine($"options: rlimit {RESOURCE_LIMIT}, hard cap {HARD_CAP_SECONDS} s (fuel {(long)(HARD_CAP_SECONDS * FUEL_PER_SECOND)}), memory {MEMORY_MEGABYTES} MB");
        text.AppendLine("id       logic   status band   comp s   verdict   reason   ref s    wall s   wall/comp  peak MiB  resource count  validation");
        foreach (var result in m_results)
        {
            var row = result.Row;
            if (result.Outcome == null)
            {
                text.AppendLine($"{row.Id,-8} {row.Logic,-7} {row.Status,-6} {row.Band,-6} {row.Seconds,7:F1}   {result.Fault}");
                continue;
            }

            var outcome = result.Outcome;
            var wall = outcome.WallMs / 1000.0;
            text.AppendLine($"{row.Id,-8} {row.Logic,-7} {row.Status,-6} {row.Band,-6} {row.Seconds,7:F1}   {outcome.Verdict,-8}  {outcome.Reason,-7} {outcome.Fuel / FUEL_PER_SECOND,7:F1}  {wall,7:F1}   {wall / row.Seconds,8:F2}  {outcome.PeakMemoryBytes / 1048576.0,8:F0}  {outcome.ResourceCount,14}  {result.Validation?.Class.ToString() ?? "-"}");
        }

        var decided = m_results.Where(result => result.Outcome is { Verdict: "sat" or "unsat" }).ToList();
        if (decided.Count > 0)
        {
            var ratios = decided.Select(result => result.Outcome!.WallMs / 1000.0 / result.Row.Seconds).Order().ToList();
            var reference = decided.Select(result => result.Outcome!.Fuel / FUEL_PER_SECOND / result.Row.Seconds).Order().ToList();
            text.AppendLine($"fuel in all {m_results.Sum(result => result.Outcome?.Fuel ?? 0)}, the most of one problem {decided.Max(result => result.Outcome!.Fuel)}");
            text.AppendLine($"decided {decided.Count} of {m_results.Count}; wall / competition median {ratios[ratios.Count / 2]:F2}, the most {ratios[^1]:F2}; reference seconds / competition median {reference[reference.Count / 2]:F2}, the most {reference[^1]:F2}");
        }

        return text.ToString();
    }

    private static List<SmtLongTailRow> Listing()
    {
        var lines = File.ReadAllLines(Path.Combine(TestContext.CurrentContext.TestDirectory, LISTING.Replace('/', Path.DirectorySeparatorChar)));
        var columns = lines[0].Split(SEPARATOR);
        int Column(string name) => Array.IndexOf(columns, name);

        return lines.Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split(SEPARATOR))
            .Select(cells => new SmtLongTailRow
            {
                Id = cells[Column("id")],
                Logic = cells[Column("logic")],
                Sha256 = cells[Column("sha256")],
                Status = cells[Column("status")],
                Seconds = double.Parse(cells[Column("time_s")], CultureInfo.InvariantCulture),
                Band = cells[Column("band")],
                Sample = cells[Column("sample")] == "1"
            })
            .ToList();
    }

    #endregion
}
