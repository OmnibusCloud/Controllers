using System.Globalization;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// Problems nested as deep as the reader takes them, solved by the real solver module
/// (<see cref="SmtDeepProblems"/>). The solver's own code calls itself a level of a term's nesting,
/// and how many calls fit the native stack a guest is given is the compiler's matter for the
/// machine: a row of such a problem is where two honest machines could give two digests.
/// <para>
/// They do not: every row is the one of a committed file, <c>Fixtures/Solver/deep.tsv</c>
/// (<see cref="SmtDeepGolden"/>), by its digest and its fuel, in both trap modes. The file was
/// written once, on one machine; the workflow runs this fixture on Windows, Linux and macOS, and
/// each of them must give exactly that file. What each platform gives is also stated in the
/// test's output, a line a row.
/// </para>
/// <para>
/// The file is rewritten on purpose only (another solver module, another command in a session,
/// another case, another canonical form of a row): run this fixture with the environment
/// variable <see cref="WRITE_VARIABLE"/> set to <c>1</c>; the golden test is then inconclusive.
/// </para>
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtSolverDeepNestingTests
{
    #region Constants

    /// <summary>Set to <c>1</c>, makes the golden test write the golden file instead of holding the run to it.</summary>
    public const string WRITE_VARIABLE = "WITAI_SMT_WRITE_DEEP";

    private const string HARDWARE = "hardware traps";
    private const string EXPLICIT = "explicit traps";
    private const string ANSWERED = "ok sat";

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        SmtSolverModule.Require();
        SmtSolverRig.Warm();
    }

    #endregion

    #region Problem Tests

    [Test]
    public void EveryShapeReachesTheDepthItIsAskedForAndTheDeepestIsTheReadersOwnTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var shape in SmtDeepProblems.SHAPES)
            {
                // The reader takes a problem exactly as deep as its limit, and none a list deeper:
                // the deepest case of each shape stands at the limit itself.
                var deepest = SmtSanitizer.Sanitize(SmtDeepProblems.Of(shape, SmtReaderLimits.PROBLEM_DEPTH));
                var deeper = SmtSanitizer.Sanitize(SmtDeepProblems.Of(shape, SmtReaderLimits.PROBLEM_DEPTH + 1));

                Assert.That(deepest.Refusal?.Reason, Is.Null, $"{shape} at the reader's depth");
                Assert.That(deeper.Refusal?.Code, Is.EqualTo(SmtRefusalCodes.NESTING_TOO_DEEP), $"{shape} a list deeper");
            }

            Assert.That(SmtDeepProblems.DEPTHS[^1], Is.EqualTo(SmtReaderLimits.PROBLEM_DEPTH), "the deepest case is the deepest problem the reader takes");
        });
    }

    #endregion

    #region Golden Tests

    [Test]
    public void TheRowOfEveryDeepProblemIsTheOneOfTheGoldenFileInBothTrapModesTest()
    {
        var both = !SmtSolverRig.ExplicitByDefault();
        if (both)
            SmtSolverRig.WarmExplicit();
        else
            TestContext.Progress.WriteLine("deep: this test process enforces shadow stacks, so its traps are explicit checks already: one trap mode is held to the file");

        var cases = SmtDeepProblems.SHAPES.SelectMany(shape => SmtDeepProblems.DEPTHS.Select(depth => SmtDeepProblems.Id(shape, depth))).ToList();
        var rows = new Dictionary<string, SmtResultData>();
        var other = new Dictionary<string, SmtResultData>();
        foreach (var shape in SmtDeepProblems.SHAPES)
        {
            foreach (var depth in SmtDeepProblems.DEPTHS)
            {
                var id = SmtDeepProblems.Id(shape, depth);
                var problem = SmtSolverProblems.Check(id, SmtDeepProblems.Of(shape, depth));

                rows[id] = State(id, both ? HARDWARE : EXPLICIT, SmtNodeRig.Row(problem, new SmtOptionsData()));
                if (both)
                    other[id] = State(id, EXPLICIT, SmtNodeRig.Row(problem, new SmtOptionsData(), SmtNodeRig.TICKET, new SmtSolverSetup { ExplicitTraps = true }));
            }
        }

        if (Environment.GetEnvironmentVariable(WRITE_VARIABLE) == "1")
        {
            var written = SmtDeepGolden.Write(cases.Select(id => (id, rows[id].Digest, rows[id].Fuel)));
            Assert.Inconclusive($"the golden file was written from this run, not compared with it: {written}");
        }

        var golden = SmtDeepGolden.Read();

        Assert.That(golden.Select(line => line.Id), Is.EqualTo(cases), $"{SmtDeepGolden.FILE} names every case, in the order of the cases");
        Assert.Multiple(() =>
        {
            foreach (var (id, digest, fuel) in golden)
            {
                Assert.That(SmtNodeRig.Words(rows[id]), Is.EqualTo(ANSWERED), $"{id}: the solver answers, on every platform: no stack ends it");
                Assert.That(rows[id].Fuel, Is.EqualTo(fuel), $"{id}: the fuel");
                Assert.That(rows[id].Digest, Is.EqualTo(digest), $"{id}: the digest of its row");
                Assert.That(SmtResultDigest.Holds(rows[id]), Is.True, id);

                if (both)
                {
                    Assert.That(other[id].Fuel, Is.EqualTo(fuel), $"{id}: the fuel, with explicit trap checks");
                    Assert.That(other[id].Digest, Is.EqualTo(digest), $"{id}: the digest of its row, with explicit trap checks");
                }
            }
        });
    }

    #endregion

    #region Tools

    /// <summary>Writes a row's line to the test's output, so that a platform's log says what it gave, and hands the row on.</summary>
    private static SmtResultData State(string id, string mode, SmtResultData row)
    {
        var code = row.Code.Length > 0 ? $" {row.Code}" : "";
        TestContext.Progress.WriteLine(
            $"deep: {SmtNodeRig.Platform,-10} {mode,-14} {id,-28} status {row.Status}{code} verdict {row.Verdict} reason {row.Reason} "
            + $"fuel {row.Fuel.ToString(CultureInfo.InvariantCulture),12} digest {row.Digest} peak {(row.PeakMemoryBytes >> 20).ToString(CultureInfo.InvariantCulture),4} MiB wall {row.WallMs.ToString(CultureInfo.InvariantCulture),6} ms");

        return row;
    }

    #endregion
}
