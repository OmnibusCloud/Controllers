using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Adapters;

/// <summary>
/// A host activity of a cancelled job stops: the job's cancellation is looked at before every
/// package, row and model the activity reads, the activity ends cancelled (the engine's word for
/// it), and its spool, its working files and its working folder go with it. Nothing is returned
/// and nothing is stored. The activities run through the engine's entry of their adapters, with a
/// job, a temp storage and variables of the tests' own, on rows made up as a node writes them.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SmtHostActivityCancelTests
{
    #region Constants

    private const string NON_NEGATIVE = "(declare-const x Int)(assert (>= x 0))";

    // An honest model that is checked in a few tens of milliseconds: its function calls itself
    // twice a level, sixteen levels down, and gives 0.
    private const string COSTLY = "((define-fun h!0 ((n Int)) Int (ite (<= n 0) 0 (+ (h!0 (- n 1)) (h!0 (- n 1))))) (define-fun x () Int (h!0 16)))";

    private const int PROBLEMS = 640;
    private const int PER_PACKAGE = 16;

    // How long a settle may go on once its job is cancelled: it stops before the next row, and a
    // row here is checked in tens of milliseconds.
    private const int STOP_BOUND_MS = 2_000;
    private const int LOOP_STARTED_TIMEOUT_MS = 120_000;
    private const int IN_THE_ROWS_MS = 1_000;
    private const int POLL_MS = 10;

    private const string RESULTS_SECTION = "*.results";

    #endregion

    #region Fields

    private SmtSettleRig m_rig = null!;
    private SmtTestTempStorage m_storage = null!;
    private SmtTestProcessingManager m_manager = null!;
    private string m_folder = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_rig = new SmtSettleRig();
        m_folder = Path.Combine(Path.GetTempPath(), $"witai_smt_cancel_{Guid.NewGuid():N}");
        m_storage = new SmtTestTempStorage(m_folder);
        m_manager = new SmtTestProcessingManager();
    }

    [TearDown]
    public void TearDown()
    {
        m_rig.Dispose();
        SmtRigModules.TryDelete(m_folder);
    }

    #endregion

    #region Settle Tests

    [Test]
    [CancelAfter(300_000)]
    public async Task SettleCancelledInTheMiddleOfItsRowsStopsAndLeavesNoFileTest()
    {
        var (set, options, packages) = Round(PROBLEMS, rate: 0);
        var solved = packages.Select(package => m_rig.Answer(package, _ => SmtSettleRig.Sat(COSTLY))).ToList();
        var problems = await m_rig.Blobs.UploadBytesAsync(Encoding.UTF8.GetBytes(set), "problems.jsonl");
        var blobs = m_rig.Blobs.Count;
        var pool = new SmtTestVariables();

        var run = SmtHostAdapterRig.Settle(problems, options, packages, solved, Host(), pool);

        // The rows are ruled on once the results' working file is there; a second later the
        // settle is among its rows, far from their end.
        var started = Stopwatch.StartNew();
        while (!Directory.EnumerateFiles(m_folder, RESULTS_SECTION, SearchOption.AllDirectories).Any())
        {
            Assert.That(run.IsCompleted, Is.False, "the settle ended before it ruled on its rows");
            Assert.That(started.ElapsedMilliseconds, Is.LessThan(LOOP_STARTED_TIMEOUT_MS), "the settle did not reach its rows");
            await Task.Delay(POLL_MS);
        }

        await Task.Delay(IN_THE_ROWS_MS);
        Assert.That(run.IsCompleted, Is.False, "the settle is still among its rows");

        var stopping = Stopwatch.StartNew();
        m_manager.Cancel();
        var status = await run;
        stopping.Stop();

        TestContext.Out.WriteLine($"stopped {stopping.ElapsedMilliseconds} ms after the job was cancelled");
        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Cancelled), status.ToString());
            Assert.That(stopping.ElapsedMilliseconds, Is.LessThan(STOP_BOUND_MS), "stopped before the next row");
            Assert.That(m_storage.Left, Is.Empty, "the working folder is gone");
            Assert.That(Directory.EnumerateFiles(m_folder, "*", SearchOption.AllDirectories), Is.Empty, "no working file is left");
            Assert.That(pool.Of(SmtHostAdapterRig.RETURNED), Is.Null, "nothing was returned");
            Assert.That(m_rig.Blobs.Count, Is.EqualTo(blobs), "no ledger was stored");
        });
    }

    [Test]
    public async Task SettleOfACancelledJobDoesNotStartTest()
    {
        var (set, options, packages) = Round(PER_PACKAGE, rate: 0);
        var solved = packages.Select(package => m_rig.Answer(package, _ => SmtSettleRig.Sat(COSTLY))).ToList();
        var problems = await m_rig.Blobs.UploadBytesAsync(Encoding.UTF8.GetBytes(set), "problems.jsonl");
        var pool = new SmtTestVariables();

        m_manager.Cancel();
        var status = await SmtHostAdapterRig.Settle(problems, options, packages, solved, Host(), pool);

        AssertCancelled(status, pool);
    }

    #endregion

    #region Other Activity Tests

    [Test]
    public async Task DisputesOfACancelledJobStopTest()
    {
        var (_, _, packages) = Round(PER_PACKAGE, rate: 0);
        var solved = packages.Select(package => m_rig.Answer(package, _ => SmtSettleRig.Sat(COSTLY))).ToList();
        var pool = new SmtTestVariables();

        m_manager.Cancel();
        var status = await SmtHostAdapterRig.Disputes(packages, solved, Host(), pool);

        AssertCancelled(status, pool);
    }

    [Test]
    public async Task RepairsOfACancelledJobStopTest()
    {
        var (_, _, packages) = Round(PER_PACKAGE, rate: 0);
        var pool = new SmtTestVariables();

        m_manager.Cancel();
        var status = await SmtHostAdapterRig.Repairs(packages, [], Host(), pool);

        AssertCancelled(status, pool);
    }

    [Test]
    public async Task SplitOfACancelledJobStopsTest()
    {
        var (set, options, _) = Round(PER_PACKAGE, rate: 0);
        var problems = await m_rig.Blobs.UploadBytesAsync(Encoding.UTF8.GetBytes(set), "problems.jsonl");
        var pool = new SmtTestVariables();

        m_manager.Cancel();
        var status = await SmtHostAdapterRig.Split(problems, options, Host(), pool);

        AssertCancelled(status, pool);
    }

    [Test]
    public async Task PreflightOfACancelledJobStopsTest()
    {
        var (set, options, _) = Round(PER_PACKAGE, rate: 0);
        var problems = await m_rig.Blobs.UploadBytesAsync(Encoding.UTF8.GetBytes(set), "problems.jsonl");
        var pool = new SmtTestVariables();

        m_manager.Cancel();
        var status = await SmtHostAdapterRig.Preflight(problems, options, Host(), pool);

        AssertCancelled(status, pool);
    }

    #endregion

    #region Tools

    private (IWitBlobService Blobs, IWitProcessingManager Manager, IWitTempStorage Storage) Host()
    {
        return (m_rig.Blobs, m_manager, m_storage);
    }

    private void AssertCancelled(IWitProcessingStatus status, SmtTestVariables pool)
    {
        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Cancelled), status.ToString());
            Assert.That(m_storage.Left, Is.Empty, "the working folder is gone");
            Assert.That(pool.Of(SmtHostAdapterRig.RETURNED), Is.Null, "nothing was returned");
        });
    }

    /// <summary>A set of problems of one text, each its own row, read with options of a rate, and its packages of sixteen.</summary>
    private (string Set, SmtOptionsData Options, List<SmtTaskPackageData> Packages) Round(int count, double rate)
    {
        var options = new SmtOptionsData { SamplingRate = rate };
        var set = SmtSets.Of(Enumerable.Range(0, count).Select(index => JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = $"p{index}", ["smt2"] = NON_NEGATIVE })));
        var packages = SmtSettleRig.Read(set, options).Chunk(PER_PACKAGE).Select(chunk => m_rig.Publish(chunk, options)).ToList();
        return (set, options, packages);
    }

    #endregion
}
