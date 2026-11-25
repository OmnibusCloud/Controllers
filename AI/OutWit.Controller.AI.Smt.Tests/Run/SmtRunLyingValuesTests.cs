using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Run;

/// <summary>
/// SmtRun against a node that solves honestly and then reports other values, end to end through
/// the engine: three in-process nodes really solve a set whose rows ask for values, and the liar
/// keeps every model the solver gave and changes the values beside it (of a check the value of a
/// term that is no constant, of an enumeration the values of one model for another's). Sampled
/// or not, the job completes, every settled row is the honest answer with the honest values,
/// and the integrity document counts what was done.
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtRunLyingValuesTests : SmtRunTestBase
{
    #region Constants

    /// <summary>As many as the other lying-node tests run on: three packages for the three nodes, one of which lies.</summary>
    private const int PROBLEMS = 40;

    #endregion

    #region Fields

    private string m_set = null!;
    private Task<SmtRunDocuments>? m_honest;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_set = SmtSets.Of(Enumerable.Range(0, PROBLEMS).Select(Line));
    }

    [SetUp]
    public void Reset()
    {
        Liars.Lie = null;
        Liars.Liars = SmtLiars.First;
        Liars.Lies = 0;
    }

    #endregion

    #region Lying Tests

    [Test]
    public async Task AnHonestRunValidatesEveryRowWithItsValuesTest()
    {
        var run = await HonestAsync();
        var sat = run.Rows.Where(row => row.GetProperty("verdict").GetString() == SmtResultWords.SAT).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sat, Has.Count.EqualTo(PROBLEMS), "every problem of the set has a model and values");
            Assert.That(sat.Select(row => row.GetProperty("validation").GetString()), Has.All.EqualTo(SmtRunWords.VALIDATED));
            Assert.That(run.IntegrityCount("lies") + run.IntegrityCount("disputes") + run.IntegrityCount("failed_models"), Is.Zero);
        });

        SmtRunChecks.AssertSchemas(run);
    }

    [TestCase(1.0)]
    [TestCase(0.0)]
    public async Task ANodeThatReportsOtherValuesBesideAnHonestModelIsFoundOutTest(double rate)
    {
        // The row's model is the solver's and satisfies the problem. Copied, the row disagrees
        // with the honest copy by its digest; alone, on one machine's word, it is the evaluator
        // that finds the value that is not its term's. Either way three more machines settle it.
        Liars.Lie = honest => Task.FromResult<SmtResultPackageData?>(SmtRunRig.Forge(Blobs, honest, rows =>
        {
            foreach (var row in rows.Where(row => row.Verdict == SmtResultWords.SAT && row.Values.Count > 0))
            {
                row.Values = row.Query == SmtQuery.Enumerate ? [.. row.Values.Skip(1), row.Values[0]] : [ShiftLast(row.Values[0])];
                row.Digest = SmtResultDigest.Of(row);
            }
        }));

        var run = await RunAsync(m_set, new SmtOptionsData { SamplingRate = rate });
        var honest = await HonestAsync();

        Assert.Multiple(() =>
        {
            Assert.That(Liars.Lies, Is.GreaterThan(0), "the liar answered");
            Assert.That(run.Rows, Has.Count.EqualTo(PROBLEMS));
            foreach (var row in honest.Rows)
            {
                var index = row.GetProperty("index").GetInt32();
                var settled = run.Row(index);
                Assert.That(settled.GetProperty("digest").GetString(), Is.EqualTo(row.GetProperty("digest").GetString()), $"problem {index}: the honest row");
                Assert.That(settled.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.VALIDATED), $"problem {index}");
                Assert.That(settled.GetRawText(), Does.Contain("\"values\":"), $"problem {index}");
                Assert.That(Values(settled), Is.EqualTo(Values(row)), $"problem {index}: the honest values");
            }

            Assert.That(run.IntegrityCount("lies"), Is.GreaterThan(0), "the integrity document counts the lies");
            Assert.That(run.IntegrityCount("failed_models"), Is.GreaterThan(0), "and the answers the evaluator called failed by a value");
            Assert.That(run.IntegrityCount("disputes_resolved"), Is.EqualTo(run.IntegrityCount("disputes")), "two honest machines of three settle every dispute");
            Assert.That(run.IntegrityCount("evaluator_solver_disagreements"), Is.Zero, "honest machines and the evaluator agree");
        });

        if (rate == 0)
            Assert.That(run.IntegrityCount("dispute_causes", SmtRunWords.MODEL_FAILED), Is.GreaterThan(0), "alone, the value that is not its term's sent the problem on");

        SmtRunChecks.AssertSchemas(run);
        SmtRunChecks.AssertNamesNoMachine(run);
    }

    #endregion

    #region Tools

    /// <summary>
    /// A problem of the set: a check that asks for the values of a constant and of two terms, and
    /// an enumeration of two models told apart by two constants, by turns.
    /// </summary>
    private static string Line(int index)
    {
        const string LIA = "(set-logic QF_LIA)(declare-const x Int)(declare-const y Int)";

        return index % 2 == 0
            ? Row(index, "check", $"{LIA}(assert (> x {index}))(assert (< x {index + 5}))(assert (= y (* 2 x)))", ",\"values\":[\"x\",\"(+ x y)\",\"(- (* 3 y) x)\"]")
            : Row(index, "enumerate", $"{LIA}(assert (and (> x {index}) (< x {index + 3})))(assert (= y (+ x 1)))", ",\"project\":[\"x\",\"y\"],\"max_models\":5");
    }

    private static string Row(int index, string query, string smt2, string extra)
    {
        return $"{{\"id\":\"q{index}\",\"query\":\"{query}\",\"smt2\":\"{smt2}\"{extra}}}";
    }

    /// <summary>An answer of values with the last number in it moved by one: the value of its last term.</summary>
    private static string ShiftLast(string values)
    {
        var end = values.Length;
        while (end > 0 && !char.IsAsciiDigit(values[end - 1]))
            end--;

        var start = end;
        while (start > 0 && char.IsAsciiDigit(values[start - 1]))
            start--;

        return start == end ? values + " " : values[..start] + (long.Parse(values[start..end]) + 1) + values[end..];
    }

    /// <summary>The values a row of the results shows: a check's one answer, or the answer of each model of an enumeration.</summary>
    private static string Values(System.Text.Json.JsonElement row)
    {
        return row.TryGetProperty("models", out var models)
            ? string.Join("|", models.EnumerateArray().Select(model => model.GetProperty("values").GetString()))
            : row.GetProperty("values").GetString() ?? "";
    }

    private Task<SmtRunDocuments> HonestAsync()
    {
        return m_honest ??= RunHonestAsync();
    }

    private async Task<SmtRunDocuments> RunHonestAsync()
    {
        var lie = Liars.Lie;
        Liars.Lie = null;
        try
        {
            return await RunAsync(m_set, new SmtOptionsData { SamplingRate = 1 });
        }
        finally
        {
            Liars.Lie = lie;
        }
    }

    #endregion
}
