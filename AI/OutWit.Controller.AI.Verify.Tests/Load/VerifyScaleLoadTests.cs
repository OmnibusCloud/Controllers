using System.Diagnostics;
using System.Text.Json;
using MemoryPack;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tests.Mock;
using OutWit.Controller.AI.Verify.Tests.Scripts;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Load;

/// <summary>
/// A hundred thousand tasks through the bundled VerifyRun on the in-process nodes, with the
/// memory and every payload measured (Category=Load; hardening batch B, item B3). The claim it
/// holds: nothing that grows with the taskset is an activity parameter or a return value, and
/// nothing of it is read whole into memory.
/// <list type="bullet">
/// <item>The taskset is 200 MiB and is never in memory: it is made as it is written to its blob.</item>
/// <item>What a share carries to a node and what a node returns is measured on the door of the
/// rig's nodes (<see cref="VerifyMeasuringNodesManager"/>), as the bytes it has on the wire.</item>
/// <item>The managed heap and the process's working set are sampled every few milliseconds for
/// the whole run. The rig holds the server and three nodes in one process, so the high-water
/// mark is an upper bound for either side.</item>
/// </list>
/// The one thing that still grows with the taskset in a message is the list of package
/// references a job carries (Grid.ForEach fans out over a collection): one reference a package,
/// and the test states its size.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Load")]
public sealed class VerifyScaleLoadTests : VerifyScriptTestBase
{
    #region Constants

    private const int TASKS = 100_000;
    private const int PADDING = 2000;
    private const int BATCH_SIZE = 64;
    private const double SAMPLING = 0.05;
    private const int SAMPLE_EVERY_MS = 10;

    private const long KIBIBYTE = 1024;
    private const long MEBIBYTE = 1024 * KIBIBYTE;

    // The bounds the run is held to. A reference to a package is some two hundred bytes on the
    // wire; the split of a 200 MiB taskset may keep a line, a batch and a package; the whole
    // process, server and nodes together, stays far below the taskset it works through twice.
    private const long MAX_REFERENCE_BYTES = 320;
    private const long MAX_SPLIT_HEAP_BYTES = 128 * MEBIBYTE;
    private const long MAX_RUN_HEAP_BYTES = 512 * MEBIBYTE;
    private const int PLATFORM_VALUE_BYTES = 64;

    #endregion

    #region Fields

    private VerifyMeasuringNodesManager m_nodes = null!;

    #endregion

    #region Tests

    [Test]
    [CancelAfter(3_600_000)]
    public async Task B3AHundredThousandTasksTravelAsBlobsEndToEndTest()
    {
        // Two runtimes turn about, so the plan has to group what the taskset interleaves; every
        // task carries 2 KiB of text, so the taskset is far larger than what may be held of it.
        var padding = new string('#', PADDING);
        var tasksetPath = Path.Combine(Path.GetTempPath(), $"witai_verify_scale_{Guid.NewGuid():N}.jsonl");
        long tasksetBytes;
        Guid tasksetBlob;
        try
        {
            using (var taskset = new VerifySyntheticTaskset(TASKS, index => index % 2 == 0
                       ? $$"""{"index":{{index}},"runtime":"quickjs-0.15.1","sources":{"m.js":"console.log({{index}} * 3) // {{padding}}"},"suite":[{"expected_stdout":"{{index * 3L}}\n"}]}"""
                       : $$"""{"index":{{index}},"runtime":"lua-5.4.8","sources":{"m.lua":"print({{index}} * 3) -- {{padding}}"},"suite":[{"expected_stdout":"{{index * 3L}}\n"}]}"""))
            using (var file = File.Create(tasksetPath))
            {
                taskset.CopyTo(file);
                tasksetBytes = taskset.Bytes;
            }

            tasksetBlob = await BlobService.UploadFileAsync(tasksetPath);
        }
        finally
        {
            File.Delete(tasksetPath);
        }

        var job = Engine.Compile(LoadScript("VerifyRun.wit"));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var heapBefore = GC.GetTotalMemory(forceFullCollection: true);
        using var process = Process.GetCurrentProcess();
        var workingSetBefore = process.WorkingSet64;

        using var meter = new VerifyMemoryMeter(SAMPLE_EVERY_MS, () => m_nodes.FirstShareAt != null);
        var clock = Stopwatch.StartNew();
        var status = await Engine.ScheduleAndWaitAsync(job, tasksetBlob, new VerifyOptionsData { BatchSize = BATCH_SIZE, SamplingRate = SAMPLING });
        clock.Stop();
        meter.Stop();

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());

        // What the job carries in its variables, as it would be serialized.
        var variables = new List<(string Name, int Items, long Bytes)>();
        foreach (var variable in job.Variables)
            variables.Add(Weigh(variable));

        var packages = variables.Single(variable => variable.Name == "packages");
        var results = variables.Single(variable => variable.Name == "results");

        var documents = new Dictionary<string, long>();
        foreach (var name in new[] { "ledger", "report", "outputs", "provenance", "integrity" })
            documents[name] = new FileInfo(await BlobService.GetLocalPathAsync((Guid)job.Variables[name].Value!)).Length;

        long taskBlobBytes = 0;
        long resultBlobBytes = 0;
        foreach (var package in (IEnumerable<VerifyTaskPackageData>)job.Variables["packages"].Value!)
            taskBlobBytes += package.SizeBytes;
        foreach (var package in (IEnumerable<VerifyResultPackageData>)job.Variables["results"].Value!)
            resultBlobBytes += package.SizeBytes;

        using var report = await ReadJsonAsync((Guid)job.Variables["report"].Value!);
        using var integrity = await ReadJsonAsync((Guid)job.Variables["integrity"].Value!);
        var summary = report.RootElement.GetProperty("summary");
        var sampled = integrity.RootElement.GetProperty("summary");

        var splitHeap = meter.PeakHeapBeforeMark - heapBefore;
        var runHeap = meter.PeakHeap - heapBefore;

        TestContext.Out.WriteLine($"""
            {TASKS} tasks ({tasksetBytes / MEBIBYTE} MiB of taskset) in batches of {BATCH_SIZE}, sampling {SAMPLING}: {clock.Elapsed.TotalSeconds:F1} s end to end, {TASKS / clock.Elapsed.TotalSeconds:F0} tasks/s
            memory, the server and three nodes in one process:
              managed heap, high-water mark while the taskset was split: {splitHeap / MEBIBYTE} MiB above the start ({meter.PeakHeapBeforeMark / MEBIBYTE} MiB)
              managed heap, high-water mark of the whole run:            {runHeap / MEBIBYTE} MiB above the start ({meter.PeakHeap / MEBIBYTE} MiB)
              working set, high-water mark of the whole run:             {meter.PeakWorkingSet / MEBIBYTE} MiB ({workingSetBefore / MEBIBYTE} MiB at the start), {meter.Samples} samples
            what travels in messages:
              to nodes:   {m_nodes.Shares} shares, {m_nodes.Requests} requests, {m_nodes.SentBytes} bytes in all; the largest share {m_nodes.LargestShareBytes} bytes in {m_nodes.LargestShareRequests} requests; the largest request {m_nodes.LargestRequestBytes} bytes
              from nodes: {m_nodes.ReturnedBytes} bytes in all; the largest return of a share {m_nodes.LargestReturnBytes} bytes
              values weighed as a word each: {string.Join(", ", m_nodes.OtherTypes)}
            what the job carries in its variables:
            {string.Join("\n", variables.Select(variable => $"  {variable.Name,-11} {variable.Items,6} item(s) {variable.Bytes,10} bytes"))}
            what travels as blobs:
              task packages   {packages.Items,6} blobs {taskBlobBytes,12} bytes
              result packages {results.Items,6} blobs {resultBlobBytes,12} bytes
            {string.Join("\n", documents.Select(document => $"  {document.Key,-15} {document.Value,25} bytes"))}
            settled: total {summary.GetProperty("total").GetInt32()}, pass {summary.GetProperty("pass").GetInt32()}, sampled {sampled.GetProperty("sampled").GetInt32()}, mismatches {sampled.GetProperty("mismatches").GetInt32()}
            """);

        Assert.Multiple(() =>
        {
            // The run did the work.
            Assert.That(summary.GetProperty("total").GetInt32(), Is.EqualTo(TASKS));
            Assert.That(summary.GetProperty("pass").GetInt32(), Is.EqualTo(TASKS));
            Assert.That(sampled.GetProperty("mismatches").GetInt32(), Is.Zero);
            Assert.That(sampled.GetProperty("sampled").GetInt32(), Is.GreaterThan(0));
            Assert.That(tasksetBytes, Is.GreaterThan(200 * MEBIBYTE));

            // A message carries references, whatever the taskset: the largest request is one
            // reference, and a node's whole share is its packages' references and nothing else.
            Assert.That(m_nodes.LargestRequestBytes, Is.LessThanOrEqualTo(MAX_REFERENCE_BYTES + 64), "one request: one package reference and the platform's own words");
            Assert.That(m_nodes.SentBytes, Is.LessThanOrEqualTo(m_nodes.Requests * (MAX_REFERENCE_BYTES + 64)));
            Assert.That(m_nodes.ReturnedBytes, Is.LessThanOrEqualTo(m_nodes.Requests * MAX_REFERENCE_BYTES));

            // What the job carries: the two lists of references grow by one reference a package,
            // and every other variable is a blob's id, a number or the options.
            Assert.That(packages.Bytes, Is.LessThanOrEqualTo(packages.Items * MAX_REFERENCE_BYTES));
            Assert.That(results.Bytes, Is.LessThanOrEqualTo(results.Items * MAX_REFERENCE_BYTES));
            Assert.That(variables.Where(variable => variable.Name is not ("packages" or "results")).Sum(variable => variable.Bytes), Is.LessThan(4 * KIBIBYTE),
                "everything else a job carries is a few words");
            Assert.That(packages.Bytes + results.Bytes, Is.LessThan(tasksetBytes / 100), "the references are a small part of a per cent of the taskset");

            // Nothing of the taskset is read whole: the split of 200 MiB keeps a line, a batch and
            // a package, and the whole run stays far below the taskset.
            Assert.That(splitHeap, Is.LessThan(MAX_SPLIT_HEAP_BYTES), "the managed heap while the taskset is split");
            Assert.That(runHeap, Is.LessThan(MAX_RUN_HEAP_BYTES), "the managed heap of the whole run, server and nodes together");
        });
    }

    #endregion

    #region Tools

    protected override IWitNodesManager CreateNodesManager(IWitEngineNode node)
    {
        m_nodes = new VerifyMeasuringNodesManager(node);
        return m_nodes;
    }

    /// <summary>A job variable's weight: the items it holds and their bytes on the wire.</summary>
    private static (string Name, int Items, long Bytes) Weigh(IWitVariable variable)
    {
        switch (variable.Value)
        {
            case null:
                return (variable.Name, 0, 0);
            case IEnumerable<VerifyTaskPackageData> packages:
                var taskReferences = packages.ToList();
                return (variable.Name, taskReferences.Count, taskReferences.Sum(item => (long)MemoryPackSerializer.Serialize(item).Length));
            case IEnumerable<VerifyResultPackageData> packages:
                var resultReferences = packages.ToList();
                return (variable.Name, resultReferences.Count, resultReferences.Sum(item => (long)MemoryPackSerializer.Serialize(item).Length));
            case VerifyOptionsData options:
                return (variable.Name, 1, MemoryPackSerializer.Serialize(options).Length);
            case Guid:
                return (variable.Name, 1, 16);
            case double:
                return (variable.Name, 1, sizeof(double));
            default:
                // The platform's own options for a fan-out (how many copies, at what rate): a few words.
                return (variable.Name, 1, PLATFORM_VALUE_BYTES);
        }
    }

    #endregion
}
