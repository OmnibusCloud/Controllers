using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// Where a session's envelope ends it, with the real solver module: the fuel of the hard cap at
/// exactly the budget, the solver's own memory ceiling, a ceiling too small for the solver to
/// start in, the cap of the output, the wall-clock backstop (a fault of the node, never a
/// verdict), a cancelled session. Needs the module (<see cref="SmtSolverModule"/>).
/// </summary>
[TestFixture]
[Category("Solver")]
public sealed class SmtSolverLimitTests
{
    #region Constants

    /// <summary>The solver's greatest resource limit: a check that only another budget ends.</summary>
    private const long NO_RESOURCE_LIMIT_TO_SPEAK_OF = 4_294_967_295;

    private const long FUEL_PER_SECOND = 5_000_000_000;
    private const long MEGABYTE = 1L << 20;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        SmtSolverModule.Require();
        SmtSolverRig.Warm();
    }

    #endregion

    #region Fuel Tests

    [Test]
    public void TheHardCapEndsASessionAtExactlyItsFuelTest()
    {
        var problem = SmtSolverProblems.Check("pigeonhole", SmtSolverProblems.Pigeonhole(11));
        var options = new SmtOptionsData { ResourceLimit = NO_RESOURCE_LIMIT_TO_SPEAK_OF, HardCapSeconds = 1 };

        var solved = SmtSolverRig.Solve(problem, options);
        var again = SmtSolverRig.Solve(problem, options);

        TestContext.Out.WriteLine($"{solved}: {solved.Fuel / Math.Max(1, solved.WallMs) * 1000:N0} units of fuel a second on this machine");
        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("ok unknown fuel"), solved.ToString());
            Assert.That(solved.Fuel, Is.EqualTo(FUEL_PER_SECOND), "one second of the reference rate, to the unit");
            Assert.That(solved.Checks, Is.EqualTo(1));
            Assert.That(solved.ReasonText, Is.Empty, "the solver was not asked: it had no fuel to answer with");
            Assert.That(again.Canonical, Is.EqualTo(solved.Canonical), "the same end at the same place");
        });
    }

    [Test]
    public void ASessionWithFuelLeftForItsAnswerIsNotEndedByItTest()
    {
        // The same problem under a resource limit it reaches long before the fuel.
        var solved = SmtSolverRig.Solve(SmtSolverProblems.Check("pigeonhole", SmtSolverProblems.Pigeonhole(11)), new SmtOptionsData { ResourceLimit = 100_000, HardCapSeconds = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("ok unknown rlimit"));
            Assert.That(solved.Fuel, Is.LessThan(FUEL_PER_SECOND));
        });
    }

    #endregion

    #region Memory Tests

    [Test]
    public void TheSolversOwnMemoryCeilingEndsASessionCleanlyTest()
    {
        const int CEILING_MEGABYTES = 64;
        var problem = SmtSolverProblems.Check("wide", SmtSolverProblems.WideMultiplication(2_048));

        var solved = SmtSolverRig.Solve(problem, new SmtOptionsData { ResourceLimit = NO_RESOURCE_LIMIT_TO_SPEAK_OF, MemoryMegabytes = CEILING_MEGABYTES });

        TestContext.Out.WriteLine(solved.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("ok unknown memout"), solved.ToString());
            Assert.That(solved.Transcript, Does.EndWith("; the solver's stderr\n(error \"out of memory\")\n"), "the solver ended by its own hand, with its own line");
            Assert.That(solved.PeakMemoryBytes, Is.LessThan(CEILING_MEGABYTES * MEGABYTE * 5 / 4), "below the host's cap: the solver met its own ceiling first");
            Assert.That(solved.Fuel, Is.LessThan(60 * FUEL_PER_SECOND), "long before the fuel");
        });
    }

    [Test]
    public void ACeilingTooSmallForTheSolverToStartInIsMemoryOutNotAFaultOfTheNodeTest()
    {
        // The module's memory is nine megabytes before the solver runs an instruction. Under a
        // ceiling of one the host still lets it start (its cap stands above the ceiling by the
        // solver's fixed part), and the solver ends by its ceiling at once: the same on every
        // node, and nothing a node is to blame for.
        var problem = SmtSolverProblems.Check("small", SmtSolverCases.ONE_SOLUTION);

        var solved = SmtSolverRig.Solve(problem, new SmtOptionsData { MemoryMegabytes = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("ok unknown memout"), solved.ToString());
            Assert.That(solved.Checks, Is.Zero, "it ended before the first check");
            Assert.That(solved.Fuel, Is.InRange(1, 50_000_000));
        });
    }

    [Test]
    public void TheSameProblemUnderTheDefaultCeilingIsSolvedTest()
    {
        var solved = SmtSolverRig.Solve(SmtSolverProblems.Check("small", SmtSolverCases.ONE_SOLUTION), new SmtOptionsData());

        Assert.That(solved.Words(), Is.EqualTo("ok sat"));
    }

    #endregion

    #region Output Tests

    [Test]
    public void AModelLargerThanTheOutputCapEndsTheSessionAsOutputExceededTest()
    {
        const int CAP = 4_096;
        var text = SmtSolverProblems.ManyConstants(200);
        var problem = SmtSolverProblems.Check("many", text);

        var capped = SmtSolverRig.Solve(problem, new SmtOptionsData(), new SmtSolverSetup { OutputLimitBytes = CAP });
        var whole = SmtSolverRig.Solve(problem, new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(whole.Words(), Is.EqualTo("ok sat"));
            Assert.That(whole.Models[0], Has.Length.GreaterThan(2 * CAP), "the model is larger than the cap of the first run");
            Assert.That(SmtSolverProblems.Validate(text, whole.Models[0]).Class, Is.EqualTo(SmtValidationClass.Validated));

            Assert.That(capped.Words(), Is.EqualTo("output_exceeded"), capped.ToString());
            Assert.That(capped.Verdict, Is.EqualTo("sat"), "the solver had said sat: it is the model that did not fit");
            Assert.That(capped.Models, Is.Empty, "a model that did not come whole is not kept");
            Assert.That(capped.Checks, Is.EqualTo(1));
            Assert.That(capped.ResourceCount, Is.EqualTo(whole.ResourceCount), "the statistics came before the model");
            Assert.That(capped.Fuel, Is.LessThan(whole.Fuel), "the session ended at the write that passed the cap");
        });
    }

    #endregion

    #region Node Fault Tests

    [Test]
    public void TheWallClockBackstopIsAFaultOfTheNodeTest()
    {
        const int BACKSTOP_MS = 150;
        var problem = SmtSolverProblems.Check("pigeonhole", SmtSolverProblems.Pigeonhole(11));

        var solved = SmtSolverRig.Solve(problem, new SmtOptionsData { ResourceLimit = NO_RESOURCE_LIMIT_TO_SPEAK_OF }, new SmtSolverSetup { BackstopMs = BACKSTOP_MS });

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("node_fault backstop"), solved.ToString());
            Assert.That(solved.Verdict, Is.EqualTo("none"), "the clock is never a verdict about the problem");
            Assert.That(solved.Reason, Is.EqualTo("none"));
            Assert.That(solved.Message, Does.Contain($"{BACKSTOP_MS} ms"));
            Assert.That(solved.Fuel, Is.LessThan(60 * FUEL_PER_SECOND), "there was fuel left");
        });
    }

    [Test]
    public void ASessionCancelledBeforeItStartsIsAFaultOfItsOwnKindAndRunsNothingTest()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var solved = SmtSolverRig.Solve(SmtSolverProblems.Check("small", SmtSolverCases.ONE_SOLUTION), new SmtOptionsData(), cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("node_fault cancelled"));
            Assert.That(solved.Fuel, Is.Zero);
            Assert.That(solved.Transcript, Is.Empty);
        });
    }

    [Test]
    public void ASessionCancelledWhileItRunsEndsAtTheSolversNextReadTest()
    {
        // Ninety-three checks, each a few milliseconds: the session is asked hundreds of times,
        // and ends at the first ask after the cancellation.
        const int QUEENS = 8;
        var problem = SmtSolverProblems.Enumerate("queens", SmtSolverProblems.Queens(QUEENS), 1_000, SmtSolverProblems.QueenNames(QUEENS));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));

        var solved = SmtSolverRig.Solve(problem, new SmtOptionsData(), cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("node_fault cancelled"), solved.ToString());
            Assert.That(solved.Count, Is.LessThan(92), "it did not run to its end");
            Assert.That(solved.Verdict, Is.EqualTo("none"));
        });
    }

    [Test]
    public void AFailureOfTheHostOutsideTheSolverIsAFaultOfTheNodeNamedByItsKindTest()
    {
        // A sandbox that was closed under the session: nothing the solver did, and nothing the
        // problem is to blame for. The outcome names the kind of failure and no more of it.
        var solved = SmtSolverRig.SolveInAClosedSandbox(SmtSolverProblems.Check("small", SmtSolverCases.ONE_SOLUTION), new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("node_fault host_fault"), solved.ToString());
            Assert.That(solved.Message, Is.EqualTo("the host failed outside the solver: ObjectDisposedException"));
            Assert.That(solved.Verdict, Is.EqualTo("none"));
            Assert.That(solved.Fuel, Is.Zero);
        });
    }

    [Test]
    public void AProcessWithoutASandboxSaysSoInFixedWordsTest()
    {
        const string WORDS = "the solver module (wasm/z3-5.4.1.wasm) is not in the controller's folder";

        var solved = SmtSolverRig.Solve(SmtSolverProblems.Check("small", SmtSolverCases.ONE_SOLUTION), new SmtOptionsData(), new SmtSolverSetup { WithoutSandbox = WORDS });

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("node_fault sandbox_unavailable"));
            Assert.That(solved.Message, Is.EqualTo(WORDS));
            Assert.That(solved.Verdict, Is.EqualTo("none"));
        });
    }

    #endregion
}
