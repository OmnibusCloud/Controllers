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
/// The test of the rows states what this machine gives for each case, in both trap modes, in the
/// test's output: the status, the verdict, the fuel and the row's digest. It compares them with
/// no other machine's; the workflow runs it on Windows, Linux and macOS, and the three logs side
/// by side are the comparison.
/// </para>
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtSolverDeepNestingTests
{
    #region Constants

    private const string HARDWARE = "hardware traps";
    private const string EXPLICIT = "explicit traps";

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

    #region Row Tests

    [Test]
    public void TheRowOfEveryDeepProblemIsStatedForThisPlatformTest()
    {
        var both = !SmtSolverRig.ExplicitByDefault();
        if (both)
            SmtSolverRig.WarmExplicit();
        else
            TestContext.Progress.WriteLine("deep: this test process enforces shadow stacks, so its traps are explicit checks already: one trap mode is stated");

        var rows = 0;
        foreach (var shape in SmtDeepProblems.SHAPES)
        {
            foreach (var depth in SmtDeepProblems.DEPTHS)
            {
                var id = SmtDeepProblems.Id(shape, depth);
                var problem = SmtSolverProblems.Check(id, SmtDeepProblems.Of(shape, depth));

                rows += State(id, both ? HARDWARE : EXPLICIT, SmtNodeRig.Row(problem, new SmtOptionsData()));
                if (both)
                    rows += State(id, EXPLICIT, SmtNodeRig.Row(problem, new SmtOptionsData(), SmtNodeRig.TICKET, new SmtSolverSetup { ExplicitTraps = true }));
            }
        }

        Assert.That(rows, Is.EqualTo(SmtDeepProblems.SHAPES.Count * SmtDeepProblems.DEPTHS.Count * (both ? 2 : 1)), "a row for every case in every trap mode this process has");
    }

    #endregion

    #region Tools

    /// <summary>Writes a row's line to the test's output: what the three platforms' logs are compared by.</summary>
    private static int State(string id, string mode, SmtResultData row)
    {
        var code = row.Code.Length > 0 ? $" {row.Code}" : "";
        TestContext.Progress.WriteLine(
            $"deep: {SmtNodeRig.Platform,-10} {mode,-14} {id,-28} status {row.Status}{code} verdict {row.Verdict} reason {row.Reason} "
            + $"fuel {row.Fuel.ToString(CultureInfo.InvariantCulture),12} digest {row.Digest} peak {(row.PeakMemoryBytes >> 20).ToString(CultureInfo.InvariantCulture),4} MiB wall {row.WallMs.ToString(CultureInfo.InvariantCulture),6} ms");

        return 1;
    }

    #endregion
}
