using System.Diagnostics;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Node;

/// <summary>
/// <c>Smt.SolvePackage</c> through the engine's own entry of its adapter, as a node runs it: a
/// package whose problems end every way a problem can end returns a row for each; a package the
/// node does not take returns its code and no blob; a cancelled package returns nothing, soon; the
/// share's progress counts the problems. The node's processing manager is the test's own, so the
/// test can cancel the job and read the progress.
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtSolvePackageTests
{
    #region Constants

    /// <summary>The solver's greatest resource limit: a check that only another budget ends.</summary>
    private const long NO_RESOURCE_LIMIT_TO_SPEAK_OF = 4_294_967_295;

    private const long FUEL_PER_SECOND = 5_000_000_000;

    #endregion

    #region Fields

    private string m_folder = null!;

    private SmtTestBlobService m_blobs = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        SmtSolverModule.Require();
        SmtSolverRig.Warm();
    }

    [SetUp]
    public void Setup()
    {
        m_folder = Path.Combine(Path.GetTempPath(), $"witai_smt_package_{Guid.NewGuid():N}");
        m_blobs = new SmtTestBlobService(Path.Combine(m_folder, "blobs"));
    }

    [TearDown]
    public void TearDown()
    {
        SmtRigModules.TryDelete(m_folder);
    }

    #endregion

    #region Rows Tests

    [Test]
    public async Task APackageOfProblemsThatEndEveryWayReturnsARowForEachTest()
    {
        // One set of options for all: a small memory ceiling and a hard cap of two seconds of the
        // reference machine, with no resource limit to speak of.
        var options = new SmtOptionsData { MemoryMegabytes = 64, HardCapSeconds = 2, ResourceLimit = NO_RESOURCE_LIMIT_TO_SPEAK_OF };
        var forged = SmtSolverProblems.Check("refused", SmtSolverCases.ONE_SOLUTION);
        forged.Hash = new string('0', 64);

        var problems = new List<SmtProblemData>
        {
            SmtGoldenPackages.Indexed(SmtSolverProblems.Check("ok", SmtSolverCases.ONE_SOLUTION, "x"), 10),
            SmtGoldenPackages.Indexed(forged, 11),
            SmtGoldenPackages.Indexed(SmtSolverProblems.Check("input-error", SmtSolverCases.UNDECLARED), 12),
            SmtGoldenPackages.Indexed(SmtSolverProblems.Check("out-of-fuel", SmtSolverProblems.Pigeonhole(11)), 13),
            SmtGoldenPackages.Indexed(SmtSolverProblems.Check("memout", SmtSolverProblems.WideMultiplication(2_048)), 14),
            SmtGoldenPackages.Indexed(SmtSolverProblems.Prove("proved", SmtSolverCases.ABOVE_ONE, "(> (* x x) x)"), 15)
        };

        var package = await SmtNodeRig.PublishAsync(problems, options, m_blobs, m_folder);
        var manager = new SmtTestProcessingManager();
        var (status, result) = await RunAsync(package, manager);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.FaultCode, Is.Empty);

        var rows = SmtNodeRig.ReadRows(await m_blobs.GetLocalPathAsync(result.ResultsBlobId), package.ProblemCount).ToDictionary(row => row.Id);
        foreach (var row in rows.Values.OrderBy(row => row.Index))
            TestContext.Out.WriteLine($"{row.Index} {row.Id,-12} {SmtNodeRig.Words(row),-28} fuel {row.Fuel,12} peak {row.PeakMemoryBytes,11} {row.WallMs,6} ms {row.Message}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Ticket, Is.EqualTo(package.Ticket));
            Assert.That(result.ResultCount, Is.EqualTo(6));
            Assert.That(result.Sha256, Is.EqualTo(Sha256(m_blobs, result.ResultsBlobId)));
            Assert.That(rows.Keys, Is.EquivalentTo(problems.Select(problem => problem.Id)), "every problem has its row");

            Assert.That(SmtNodeRig.Words(rows["ok"]), Is.EqualTo("ok sat"));
            Assert.That(rows["ok"].Values, Has.Count.EqualTo(1));
            Assert.That(SmtNodeRig.Words(rows["refused"]), Is.EqualTo("refused hash_mismatch"));
            Assert.That(rows["refused"].Fuel, Is.Zero, "no solver ran");
            Assert.That(SmtNodeRig.Words(rows["input-error"]), Is.EqualTo("input_error"));
            Assert.That(SmtNodeRig.Words(rows["out-of-fuel"]), Is.EqualTo("ok unknown fuel"));
            Assert.That(rows["out-of-fuel"].Fuel, Is.EqualTo(2 * FUEL_PER_SECOND), "the hard cap's fuel, to the unit");
            Assert.That(SmtNodeRig.Words(rows["memout"]), Is.EqualTo("ok unknown memout"));
            Assert.That(SmtNodeRig.Words(rows["proved"]), Is.EqualTo("ok unsat proved"));

            foreach (var row in rows.Values)
            {
                Assert.That(row.Ticket, Is.EqualTo(package.Ticket), row.Id);
                Assert.That(SmtResultDigest.Holds(row), Is.True, row.Id);
                Assert.That(row.Index, Is.EqualTo(problems.Single(problem => problem.Id == row.Id).Index), row.Id);
            }
        });

        // Every row is what a session of the problem gives on its own.
        Assert.Multiple(() =>
        {
            foreach (var problem in problems)
                Assert.That(rows[problem.Id].Digest, Is.EqualTo(SmtNodeRig.Row(problem, options).Digest), problem.Id);
        });
    }

    [Test]
    public async Task PackagesOfAShareStartedTogetherReturnEachItsOwnRowsTest()
    {
        // Three packages started at once, as the client starts a share: their problems share the
        // node's budget of sessions, a package's last problems running beside the next one's.
        // Each package returns its own rows under its own ticket, and every row is the one a
        // session of its problem gives on its own.
        var options = new SmtOptionsData();
        var share = new List<(SmtTaskPackageData Package, List<SmtProblemData> Problems)>();
        for (var package = 0; package < 3; package++)
        {
            var problems = new List<SmtProblemData>
            {
                SmtGoldenPackages.Indexed(SmtSolverProblems.Enumerate($"queens-{package}", SmtSolverProblems.Queens(6), 10, SmtSolverProblems.QueenNames(6)), 10 * package)
            };
            for (var index = 1; index < 6; index++)
                problems.Add(SmtGoldenPackages.Indexed(SmtSolverProblems.Check($"p{package}-{index}", $"(set-logic QF_LIA)(declare-const x Int)(assert (> x {10 * package + index}))", "x"), 10 * package + index));

            share.Add((await SmtNodeRig.PublishAsync(problems, options, m_blobs, m_folder), problems));
        }

        var node = SmtBudgetRig.Node();
        var manager = new SmtTestProcessingManager();
        var runs = await Task.WhenAll(share.Select(entry => RunAsync(entry.Package, manager)));
        var counts = SmtBudgetRig.Counts(node);
        TestContext.Out.WriteLine($"the node's budget: at most {counts.PeakRunning} session(s) and {counts.PeakOpen} package(s) open at once");

        for (var index = 0; index < share.Count; index++)
        {
            var (package, problems) = share[index];
            var (status, result) = runs[index];
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(result, Is.Not.Null);

            var rows = SmtNodeRig.ReadRows(await m_blobs.GetLocalPathAsync(result!.ResultsBlobId), package.ProblemCount);
            Assert.Multiple(() =>
            {
                Assert.That(result.FaultCode, Is.Empty);
                Assert.That(result.Ticket, Is.EqualTo(package.Ticket), "the result answers its own package");
                Assert.That(result.ResultCount, Is.EqualTo(problems.Count));
                Assert.That(rows.Select(row => row.Id), Is.EquivalentTo(problems.Select(problem => problem.Id)), "its own problems, each once");
                Assert.That(rows.Select(row => row.Ticket), Has.All.EqualTo(package.Ticket));

                foreach (var problem in problems)
                    Assert.That(rows.Single(row => row.Id == problem.Id).Digest, Is.EqualTo(SmtNodeRig.Row(problem, options).Digest), problem.Id);
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(counts.Running, Is.Zero, "the node's budget ends idle");
            Assert.That(counts.Open, Is.Zero);
            Assert.That(counts.Waiting, Is.Zero);
            Assert.That(counts.PeakOpen, Is.InRange(1, SmtBudgetRig.MaxOpenPackages));
        });
    }

    [Test]
    public async Task TheShareReportsItsProblemsAsTheyAreSolvedTest()
    {
        var problems = Enumerable.Range(0, 6)
            .Select(index => SmtGoldenPackages.Indexed(SmtSolverProblems.Check($"p{index}", $"(set-logic QF_LIA)(declare-const x Int)(assert (> x {index}))"), index))
            .ToList();
        var package = await SmtNodeRig.PublishAsync(problems, new SmtOptionsData(), m_blobs, m_folder);
        var manager = new SmtTestProcessingManager();

        var (status, result) = await RunAsync(package, manager);
        var reports = manager.Reports.ToList();

        foreach (var (fraction, stage) in reports)
            TestContext.Out.WriteLine($"{fraction:F2} {stage}");

        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(result?.ResultCount, Is.EqualTo(6));
            Assert.That(reports, Is.Not.Empty, "a host that can receive progress receives it");
            Assert.That(reports[0].Stage, Does.StartWith("receiving problems: 0/1 packages"));
            Assert.That(reports.Select(report => report.Fraction), Is.Ordered, "the share's progress only moves on");
            Assert.That(reports.Select(report => report.Stage), Has.Some.EqualTo("0/6 problems"), "the delivery's end is reported");
            Assert.That(reports[^1], Is.EqualTo((1.0, "6/6 problems")), "and the share's last problem");
        });
    }

    [Test]
    public async Task APackageWhoseBlobTheCacheDroppedBeforeItsTurnIsFetchedAgainTest()
    {
        // The node's blob cache drops the package's file as soon as the package has it, which is
        // before its turn on the node's budget comes: the package waits there with its blob
        // downloaded, for as long as the packages before it take, and a cache may need the room.
        var problem = SmtGoldenPackages.Indexed(SmtSolverProblems.Check("ok", SmtSolverCases.ONE_SOLUTION, "x"), 0);
        var package = await SmtNodeRig.PublishAsync([problem], new SmtOptionsData(), m_blobs, m_folder);
        var cache = new SmtTestBlobCache(m_blobs, Path.Combine(m_folder, "cache"));
        var manager = new SmtTestProcessingManager();
        manager.OnProgress = (_, stage) =>
        {
            // The report that ends the delivery: the package's blob has arrived.
            if (stage == "0/1 problems")
                cache.Drop();
        };

        var pool = new WitVariableCollection();
        var status = await SmtAdapterRig.Start(package, cache, manager, pool);
        var result = SmtAdapterRig.Result(pool);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        Assert.That(result, Is.Not.Null);

        var rows = SmtNodeRig.ReadRows(await m_blobs.GetLocalPathAsync(result!.ResultsBlobId), package.ProblemCount);
        Assert.Multiple(() =>
        {
            Assert.That(cache.Dropped, Is.EqualTo(1), "the cache dropped the file the package had been given");
            Assert.That(cache.Fetches, Is.EqualTo(2), "and the package fetched its blob again when its turn came");
            Assert.That(result.FaultCode, Is.Empty);
            Assert.That(rows.Select(row => row.Digest), Is.EqualTo(new[] { SmtNodeRig.Row(problem, new SmtOptionsData()).Digest }), "the row is the one a session of the problem gives");
        });
    }

    #endregion

    #region Fault Tests

    [Test]
    public async Task APackageTheNodeDoesNotTakeReturnsItsCodeAndNoBlobTest()
    {
        var problems = new List<SmtProblemData> { SmtGoldenPackages.Indexed(SmtSolverProblems.Check("ok", SmtSolverCases.ONE_SOLUTION), 0) };
        var package = await SmtNodeRig.PublishAsync(problems, new SmtOptionsData(), m_blobs, m_folder);
        package.RequiredControllerVersion = "0.0.1";
        var blobs = m_blobs.Count;

        var (status, result) = await RunAsync(package, new SmtTestProcessingManager());

        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), "the activity says why: it does not fail");
            Assert.That(result?.FaultCode, Is.EqualTo(SmtNodeFaults.VERSION_MISMATCH));
            Assert.That(result?.FaultMessage, Does.Contain("0.0.1"));
            Assert.That(result?.ResultsBlobId, Is.EqualTo(Guid.Empty), "no blob");
            Assert.That(result?.ResultCount, Is.Zero);
            Assert.That(result?.Ticket, Is.EqualTo(package.Ticket), "the result still answers its package");
            Assert.That(m_blobs.Count, Is.EqualTo(blobs), "nothing was uploaded");
        });
    }

    [Test]
    public async Task ABlobTheNodeCannotFetchIsRefusedAsDownloadFailedTest()
    {
        var problems = new List<SmtProblemData> { SmtGoldenPackages.Indexed(SmtSolverProblems.Check("ok", SmtSolverCases.ONE_SOLUTION), 0) };
        var package = await SmtNodeRig.PublishAsync(problems, new SmtOptionsData(), m_blobs, m_folder);
        package.ProblemsBlobId = Guid.NewGuid();

        var fault = await SmtNodeRig.FetchFaultAsync(m_blobs, package);

        Assert.That(fault, Is.EqualTo(SmtNodeFaults.DOWNLOAD_FAILED));
    }

    #endregion

    #region Cancel Tests

    [Test]
    public async Task ACancelledPackageReturnsNothingAndEndsSoonTest()
    {
        // More problems of some seconds each than a node runs at once: the cancel comes when the
        // first of them is done, with others running and others not handed out.
        const int PROBLEMS = 24;
        var problems = Enumerable.Range(0, PROBLEMS)
            .Select(index => SmtGoldenPackages.Indexed(SmtSolverProblems.Enumerate($"queens-{index}", SmtSolverProblems.Queens(8), 1_000, SmtSolverProblems.QueenNames(8)), index))
            .ToList();
        var package = await SmtNodeRig.PublishAsync(problems, new SmtOptionsData(), m_blobs, m_folder);
        var blobs = m_blobs.Count;

        var manager = new SmtTestProcessingManager();
        var cancelled = new Stopwatch();
        var once = 0;
        manager.OnProgress = (_, stage) =>
        {
            // The first report of the work with a problem done: "n/24 problems", n above 0.
            if (!stage.EndsWith(" problems", StringComparison.Ordinal) || stage.StartsWith("0/", StringComparison.Ordinal) || Interlocked.Exchange(ref once, 1) == 1)
                return;

            cancelled.Start();
            manager.Cancel();
        };

        var started = Stopwatch.StartNew();
        var (status, result) = await RunAsync(package, manager);
        var after = cancelled.Elapsed;

        var done = manager.Reports.Select(report => report.Stage).Where(stage => stage.EndsWith(" problems", StringComparison.Ordinal)).ToList();
        TestContext.Out.WriteLine($"{started.ElapsedMilliseconds} ms in all, {after.TotalMilliseconds:F0} ms after the cancel; reports: {string.Join(" | ", done)}");

        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Cancelled));
            Assert.That(result, Is.Null.Or.Property(nameof(SmtResultPackageData.ResultsBlobId)).EqualTo(Guid.Empty), "a cancelled package returns nothing");
            Assert.That(m_blobs.Count, Is.EqualTo(blobs), "and uploads nothing");
            Assert.That(after, Is.LessThan(TimeSpan.FromSeconds(30)), "the running sessions end at their next read");
            Assert.That(done, Has.None.EqualTo($"{PROBLEMS}/{PROBLEMS} problems"), "the cancel came in the middle");
        });
    }

    #endregion

    #region Tools

    private async Task<(IWitProcessingStatus Status, SmtResultPackageData? Result)> RunAsync(SmtTaskPackageData package, SmtTestProcessingManager manager)
    {
        var pool = new WitVariableCollection();
        var status = await SmtAdapterRig.Start(package, m_blobs, manager, pool);
        return (status, SmtAdapterRig.Result(pool));
    }

    private static string Sha256(SmtTestBlobService blobs, Guid blobId)
    {
        using var stream = File.OpenRead(blobs.GetLocalPathAsync(blobId).Result);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
    }

    #endregion
}
