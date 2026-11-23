using System.Diagnostics;
using System.Text;
using MemoryPack;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Controller.AI.Smt.Tests.Run;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Load;

/// <summary>
/// Ten thousand small problems through the bundled SmtRun on three in-process nodes at the default
/// sampling rate, with the memory and every payload measured (Category=Load; the build server runs
/// it on Linux, in a job of its own). The claim it holds: nothing that grows with the problem set is
/// an activity parameter or a return value, and the host does not hold the set, the packages or the
/// rows in memory: a record a copy and a few numbers a problem.
/// <list type="bullet">
/// <item>What a share carries to a node and what a node returns is measured on the door of the
/// rig's nodes (<see cref="SmtMeasuringNodesManager"/>), as the bytes it has on the wire: one
/// package reference a request, one result reference an answer.</item>
/// <item>The managed heap is read for the whole run (<see cref="SmtMemoryMeter"/>): what is live
/// after a full collection is what the bound is put on. The rig holds the host and three nodes in
/// one process, so the mark is an upper bound for either side.</item>
/// </list>
/// The number of problems can be lowered for a run by hand (<c>WITAI_SMT_LOAD_PROBLEMS</c>).
/// </summary>
[TestFixture]
[Category("Load")]
[NonParallelizable]
public sealed class SmtRunLoadTests : SmtRunTestBase
{
    #region Constants

    private const string PROBLEMS_VARIABLE = "WITAI_SMT_LOAD_PROBLEMS";
    private const int PROBLEMS = 10_000;
    private const int SAMPLE_EVERY_MS = 10;
    private const int LIVE_EVERY_MS = 250;

    private const long KIBIBYTE = 1024;
    private const long MEBIBYTE = 1024 * KIBIBYTE;

    // The bounds the run is held to. A reference to a package is a few hundred bytes on the wire
    // (the options travel in it). What the whole process holds live, host and nodes together: a
    // record a copy and a few numbers a problem, the rows of the sessions running, the compiled
    // module's managed side; far under a set of ten thousand problems' rows.
    private const long MAX_REFERENCE_BYTES = 400;
    private const long MAX_RUN_LIVE_BYTES = 192 * MEBIBYTE;
    private const int PLATFORM_VALUE_BYTES = 64;

    #endregion

    #region Fields

    private SmtMeasuringNodesManager m_nodes = null!;

    #endregion

    #region Tests

    [Test]
    [CancelAfter(1_800_000)]
    public async Task TenThousandProblemsTravelAsBlobsEndToEndTest()
    {
        var problems = Environment.GetEnvironmentVariable(PROBLEMS_VARIABLE) is { Length: > 0 } named ? int.Parse(named) : PROBLEMS;
        var set = new StringBuilder(problems * 100);
        for (var index = 0; index < problems; index++)
        {
            var smt2 = index % 3 == 2
                ? $"(set-logic QF_LIA)(declare-const x Int)(assert (> x {index}))(assert (< x {index}))"
                : $"(set-logic QF_LIA)(declare-const x Int)(assert (> x {index}))(assert (< x {index + 3}))";
            set.Append($"{{\"id\":\"p{index}\",\"smt2\":\"{smt2}\"}}\n");
        }

        var setText = set.ToString();
        var blob = await UploadAsync(setText, "problems.jsonl");
        var job = Engine.Compile(Script("SmtRun.wit"));
        var options = new SmtOptionsData { SamplingRate = SmtOptionsRules.DEFAULT_SAMPLING_RATE };

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var heapBefore = GC.GetTotalMemory(forceFullCollection: true);
        using var process = Process.GetCurrentProcess();
        var workingSetBefore = process.WorkingSet64;

        using var meter = new SmtMemoryMeter(SAMPLE_EVERY_MS, LIVE_EVERY_MS, () => m_nodes.FirstShareAt != null);
        var clock = Stopwatch.StartNew();
        var status = await Engine.ScheduleAndWaitAsync(job, blob, options);
        clock.Stop();
        meter.Stop();

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());

        var run = await SmtRunDocuments.ReadAsync(job, Blobs);
        var variables = job.Variables.Select(Weigh).ToList();
        var packages = variables.Single(variable => variable.Name == "packages");
        var solved = variables.Single(variable => variable.Name == "solved");

        var documents = new Dictionary<string, long>();
        foreach (var name in new[] { "ledger", "results", "report", "provenance", "integrity" })
            documents[name] = new FileInfo(await Blobs.GetLocalPathAsync((Guid)job.Variables[name].Value!)).Length;

        long packageBytes = 0;
        foreach (var package in (IEnumerable<SmtTaskPackageData>)job.Variables["packages"].Value!)
            packageBytes += package.SizeBytes;

        var runLive = meter.PeakLive - heapBefore;
        var (packageCount, _, selected, complete, unanswered) = run.FanOut("main");

        TestContext.Out.WriteLine($"""
            {problems} problems ({setText.Length / KIBIBYTE} KiB of set) in {packageCount} packages, sampling {options.SamplingRate}: {clock.Elapsed.TotalSeconds:F1} s end to end, {problems / clock.Elapsed.TotalSeconds:F0} problems/s
            memory, the host and three nodes in one process ({heapBefore / MEBIBYTE} MiB live at the start):
              live on the managed heap, high-water mark of the whole run: {runLive / MEBIBYTE} MiB above the start ({meter.LiveSamples} readings after a full collection)
              live on the managed heap, high-water mark while the set was split: {(meter.PeakLiveBeforeMark - heapBefore) / MEBIBYTE} MiB above the start
              managed heap as it stood, garbage included, over the whole run: {(meter.PeakHeap - heapBefore) / MEBIBYTE} MiB above the start ({meter.Samples} readings)
              working set, high-water mark: {meter.PeakWorkingSet / MEBIBYTE} MiB ({workingSetBefore / MEBIBYTE} MiB at the start)
            what travels in messages:
              to nodes:   {m_nodes.Shares} shares, {m_nodes.Requests} requests, {m_nodes.SentBytes} bytes in all; the largest share {m_nodes.LargestShareBytes} bytes; the largest request {m_nodes.LargestRequestBytes} bytes
              from nodes: {m_nodes.ReturnedBytes} bytes in all; the largest return of a share {m_nodes.LargestReturnBytes} bytes
              values weighed as a word each: {string.Join(", ", m_nodes.OtherTypes)}
            what the job carries in its variables:
            {string.Join("\n", variables.Select(variable => $"  {variable.Name,-11} {variable.Items,6} item(s) {variable.Bytes,10} bytes"))}
            what travels as blobs: packages of problems {packageBytes} bytes; documents {string.Join(", ", documents.Select(document => $"{document.Key} {document.Value}"))}
            settled: {run.Rows.Count} rows; main fan-out: {selected} of {packageCount} packages asked twice, {complete} came back twice, {unanswered} unanswered; compared {run.IntegrityCount("compared")}, agreements {run.IntegrityCount("agreements")}, disputes {run.IntegrityCount("disputes")}
            """);

        Assert.Multiple(() =>
        {
            // The run did the work, and did it as it should.
            Assert.That(run.Rows, Has.Count.EqualTo(problems));
            Assert.That(run.Rows.Count(row => row.GetProperty("verdict").GetString() == SmtResultWords.SAT), Is.EqualTo(problems - problems / 3));
            Assert.That(run.Rows.Count(row => row.GetProperty("verdict").GetString() == SmtResultWords.UNSAT), Is.EqualTo(problems / 3));
            Assert.That(run.Rows.Where(row => row.GetProperty("verdict").GetString() == SmtResultWords.SAT).Select(row => row.GetProperty("validation").GetString()), Has.All.EqualTo(SmtRunWords.VALIDATED));
            Assert.That(run.IntegrityCount("disputes") + run.IntegrityCount("no_result") + run.IntegrityCount("rejected_results"), Is.Zero);
            Assert.That(selected, Is.EqualTo(SmtRunRig.Selected(packageCount, options.SamplingRate)));
            Assert.That(complete, Is.EqualTo(selected));
            Assert.That(run.IntegrityCount("agreements"), Is.EqualTo(run.IntegrityCount("compared")));

            // A message carries references, whatever the set: a request is one package reference,
            // an answer one result reference.
            Assert.That(m_nodes.LargestRequestBytes, Is.LessThanOrEqualTo(MAX_REFERENCE_BYTES + 64), "one request: one package reference and the platform's own words");
            Assert.That(m_nodes.SentBytes, Is.LessThanOrEqualTo(m_nodes.Requests * (MAX_REFERENCE_BYTES + 64)));
            Assert.That(m_nodes.ReturnedBytes, Is.LessThanOrEqualTo(m_nodes.Requests * MAX_REFERENCE_BYTES));

            // What the job carries: the lists of references grow by one reference a package, and
            // every other variable is a blob's id, a number or the options.
            Assert.That(packages.Bytes, Is.LessThanOrEqualTo(packages.Items * MAX_REFERENCE_BYTES));
            Assert.That(solved.Bytes, Is.LessThanOrEqualTo(solved.Items * MAX_REFERENCE_BYTES));
            Assert.That(variables.Where(variable => variable.Name is not ("packages" or "solved")).Sum(variable => variable.Bytes), Is.LessThan(4 * KIBIBYTE),
                "everything else a job carries is a few words");

            Assert.That(runLive, Is.LessThan(MAX_RUN_LIVE_BYTES), "what is live over the whole run, host and nodes together");
            Assert.That(meter.LiveSamples, Is.GreaterThan(10), "the run was measured, not glanced at");
        });
    }

    #endregion

    #region Tools

    private protected override IWitNodesManager CreateNodesManager(IWitEngineNode node)
    {
        return m_nodes = new SmtMeasuringNodesManager(node);
    }

    /// <summary>A job variable's weight: the items it holds and their bytes on the wire.</summary>
    private static (string Name, int Items, long Bytes) Weigh(IWitVariable variable)
    {
        switch (variable.Value)
        {
            case null:
                return (variable.Name, 0, 0);
            case IEnumerable<SmtTaskPackageData> packages:
                var taskReferences = packages.ToList();
                return (variable.Name, taskReferences.Count, taskReferences.Sum(item => (long)MemoryPackSerializer.Serialize(item).Length));
            case IEnumerable<SmtResultPackageData> packages:
                var resultReferences = packages.ToList();
                return (variable.Name, resultReferences.Count, resultReferences.Sum(item => (long)MemoryPackSerializer.Serialize(item).Length));
            case SmtOptionsData options:
                return (variable.Name, 1, MemoryPackSerializer.Serialize(options).Length);
            case Guid:
                return (variable.Name, 1, 16);
            case double:
                return (variable.Name, 1, sizeof(double));
            default:
                return (variable.Name, 1, PLATFORM_VALUE_BYTES);
        }
    }

    #endregion
}
