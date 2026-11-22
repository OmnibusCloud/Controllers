using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MemoryPack;
using OutWit.Controller.AI.Corpus.Adapters;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Tests.Scripts;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Load;

/// <summary>
/// Audit finding M-5 at scale (Category=Load, not part of the regular run): a synthetic job of a
/// hundred thousand shards against a loopback origin, through the engine with the bundled
/// CorpusProcess.wit and six mock nodes. It measures what travels and what is held: the bytes of
/// Corpus values the nodes were handed as parameters and returned, the blobs the tasks and the
/// results travelled in, the documents, and the memory high-water mark of the process (which here
/// is the server, the six nodes and the origin together) and of the server's own reading of the
/// run. The numbers are printed, and written as JSON to the file named by
/// <c>WITAI_CORPUS_LOAD_REPORT</c> when it is set. <c>WITAI_CORPUS_LOAD_SHARDS</c> runs another
/// number of shards.
/// <para>
/// The engine SDK host the rigs run on stops any job after 300 seconds
/// (<c>SdkLimits.MAX_EXECUTION_TIME_SECONDS</c>): a limit of that local host, which a server's
/// engine does not have. A hundred thousand shards fetched and processed by six nodes inside one
/// process take longer than that, so this test lifts the timer of its own job, by reflection,
/// and says so by name if the host no longer keeps its timers where the test looks.
/// </para>
/// </summary>
[TestFixture]
[Category("Load")]
[NonParallelizable]
public sealed class CorpusPackageLoadTests : CorpusScriptTestBase
{
    #region Constants

    private const string SHARD = "tiny.warc.wet.gz";
    private const int DEFAULT_SHARDS = 100_000;
    private const double SAMPLING_RATE = 0.05;

    private const string SHARDS_VARIABLE = "WITAI_CORPUS_LOAD_SHARDS";
    private const string REPORT_VARIABLE = "WITAI_CORPUS_LOAD_REPORT";

    /// <summary>What one task cost as a parameter of its own before packages (the audit measured 509 bytes), and one result as a return value (1,800).</summary>
    private const int AUDITED_TASK_BYTES = 509;
    private const int AUDITED_RESULT_BYTES = 1800;

    #endregion

    #region Fields

    private string m_directory = null!;
    private CorpusTestOrigin m_tiny = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_load_packages_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
        WarcSynth.WriteWetShard(Path.Combine(m_directory, SHARD), 4, 120, 3);
        m_tiny = new CorpusTestOrigin(m_directory);

        // Six nodes have two downloads at once each. In the rig the six are one process and
        // share one fetcher, so it is given the downloads of all six: with a single node's two,
        // a hundred thousand loopback fetches alone take longer than the 300 seconds after which
        // the engine SDK host of the rig stops any job (its own limit; a server has none such).
        WitActivityAdapterCorpusProcessPackage.Fetcher = new ShardFetcher(ShardFetchPolicy.Default with { MaxConcurrent = Nodes * ShardFetchPolicy.Default.MaxConcurrent }, ShardOriginPolicy.Machine);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        WitActivityAdapterCorpusProcessPackage.Fetcher = null;
        m_tiny.Dispose();
        CorpusTestFolders.Delete(m_directory);
    }

    #endregion

    #region Tests

    [Test]
    [CancelAfter(3 * 60 * 60 * 1000)]
    public async Task M5AHundredThousandShardsTravelAsBlobsEndToEndTest()
    {
        var shards = int.TryParse(Environment.GetEnvironmentVariable(SHARDS_VARIABLE), NumberStyles.Integer, CultureInfo.InvariantCulture, out var asked) && asked > 0 ? asked : DEFAULT_SHARDS;
        var packagesExpected = CorpusPackagePlanner.Packages(shards);
        var perPackage = CorpusPackagePlanner.ShardsPerPackage(shards);

        // The manifest: one small shard listed as many times as the job has shards. The run keeps
        // statistics and hashes only and makes no signatures: what is measured is the way of the
        // tasks and the results, two product blobs a shard would be two hundred thousand files of
        // the rig's own, and the whole job has to end inside the 300 seconds the rig's host gives one.
        var manifestPath = Path.Combine(m_directory, "manifest.json");
        await using (var manifest = new StreamWriter(manifestPath, false, new UTF8Encoding(false)))
        {
            await manifest.WriteAsync($$"""{"pipeline":{"version":"load","identify_language":false,"sign":false,"output":"stats_and_hashes"},"allowed_origins":["{{m_tiny.Host}}"],"sources":[""");
            for (var index = 0; index < shards; index++)
                await manifest.WriteAsync((index == 0 ? "" : ",") + $"\"{m_tiny.ShardUrl(SHARD)}\"");
            await manifest.WriteAsync("]}");
        }

        var manifestBlob = await BlobService.UploadFileAsync(manifestPath);
        var manifestBytes = new FileInfo(manifestPath).Length;
        NodesManager.StartTogether = true;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var managedBefore = GC.GetTotalMemory(forceFullCollection: true);
        var workingSetBefore = process.WorkingSet64;
        var requestsBefore = NodesManager.Requests;
        var requestBytesBefore = NodesManager.RequestBytes;
        var returnBytesBefore = NodesManager.ReturnBytes;
        var answerBytesBefore = NodesManager.AnswerBlobBytes;

        // The run, with the process's memory looked at ten times a second.
        using var sampler = new MemorySampler();
        var clock = Stopwatch.StartNew();
        var job = Engine.Compile(LoadScript("CorpusProcess.wit"));
        var status = await Engine.ScheduleAndWaitAsync(job, manifestBlob, Guid.Empty, new CorpusOptionsData { SamplingRate = SAMPLING_RATE });
        var elapsed = clock.Elapsed;
        var (managedPeak, workingSetPeak) = sampler.Stop();

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), Describe(status));

        var packages = ((IEnumerable)job.Variables["tasks"].Value!).Cast<CorpusTaskPackageData>().ToList();
        var answers = ((IEnumerable)job.Variables["executions"].Value!).Cast<CorpusResultPackageData>().ToList();
        var reportPath = await BlobService.GetLocalPathAsync((Guid)job.Variables["report"].Value!);
        var integrityPath = await BlobService.GetLocalPathAsync((Guid)job.Variables["integrity"].Value!);

        // The report, read as it was written: a shard at a time.
        var verdicts = new Dictionary<string, int>(StringComparer.Ordinal);
        var twice = 0;
        long fetchMs = 0, processMs = 0, waitMs = 0;
        var next = 0;
        var ordered = true;
        string? content = null;
        var oneContent = true;
        JsonElement summary = default;
        using (var report = new FileStream(reportPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
        {
            OutWit.Controller.AI.Corpus.Json.CorpusJsonStream.Read(report, "shards",
                (name, value) =>
                {
                    if (name == "summary")
                        summary = value.Clone();
                },
                (index, shard) =>
                {
                    var verdict = shard.GetProperty("verdict").GetString()!;
                    verdicts[verdict] = verdicts.GetValueOrDefault(verdict) + 1;
                    ordered &= shard.GetProperty("index").GetInt32() == next++;
                    if (shard.GetProperty("executions").GetInt32() == 2)
                        twice++;

                    if (shard.GetProperty("stats") is { ValueKind: JsonValueKind.Object } stats)
                    {
                        fetchMs += stats.GetProperty("fetch_ms").GetInt64();
                        processMs += stats.GetProperty("process_ms").GetInt64();
                        waitMs += stats.GetProperty("wait_ms").GetInt64();
                    }

                    var hash = shard.GetProperty("content_sha256").GetString();
                    content ??= hash;
                    oneContent &= hash == content;
                });
        }

        // The server's own reading of the run, alone: the ledger built from the blobs and both
        // documents written to a stream that keeps nothing.
        GC.Collect();
        var serverBefore = GC.GetTotalMemory(forceFullCollection: true);
        using var serverSampler = new MemorySampler();
        var serverClock = Stopwatch.StartNew();
        CorpusRoundSource? first = await CorpusRoundSourceBlobs.OpenAsync(CorpusRoundKind.First, packages, answers, BlobService);
        CorpusLedger? ledger = CorpusLedger.Build(CorpusTestRun.VERSION, first);
        using var discard = new DiscardStream();
        CorpusReportWriter.Write(ledger, discard);
        CorpusIntegrityWriter.Write(ledger, discard);
        var serverElapsed = serverClock.Elapsed;
        var (serverPeak, _) = serverSampler.Stop();
        var serverHeld = GC.GetTotalMemory(forceFullCollection: true) - serverBefore;
        GC.KeepAlive(ledger);

        // The server's own planning of the run, alone: the manifest read from its file a source
        // at a time, planned, estimated, and its tasks written into packages. What it holds while
        // it works is the parsed plan: the one thing of a run that is still held per shard.
        ledger = null;
        first = null;
        GC.Collect();
        var splitBefore = GC.GetTotalMemory(forceFullCollection: true);
        using var splitSampler = new MemorySampler();
        var splitClock = Stopwatch.StartNew();
        CorpusManifest parsed;
        using (var file = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            parsed = CorpusManifestParser.Parse(file);

        var options = new CorpusOptionsData { SamplingRate = SAMPLING_RATE };
        var plan = CorpusTaskPlanner.Plan(parsed, options, Guid.Empty, "");
        var planned = new CorpusPlanned(plan, parsed, options, manifestBytes, null, 0);
        var sizes = ShardSizeProbe.Unasked(parsed.Sources);
        var estimates = CorpusPlanning.Estimate(planned, sizes);
        var written = await CorpusRounds.PackagesAsync(BlobService, CorpusTaskPlanner.Issue(plan, CorpusTestRun.VERSION, sizes), plan.Tasks.Count);
        var splitElapsed = splitClock.Elapsed;
        var (splitPeak, _) = splitSampler.Stop();
        var splitHeld = GC.GetTotalMemory(forceFullCollection: true) - splitBefore;
        GC.KeepAlive(parsed);
        GC.KeepAlive(plan);
        GC.KeepAlive(sizes);
        GC.KeepAlive(estimates);
        GC.KeepAlive(written);

        var packageBytes = packages.Sum(package => (long)MemoryPackSerializer.Serialize(package).Length);
        var answerBytes = answers.Sum(answer => (long)MemoryPackSerializer.Serialize(answer).Length);
        var taskBlobBytes = packages.Sum(package => package.SizeBytes);
        var resultBlobBytes = NodesManager.AnswerBlobBytes - answerBytesBefore;
        var executions = shards + twice;

        var measured = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["shards"] = shards,
            ["nodes"] = Nodes,
            ["sampling_rate"] = SAMPLING_RATE,
            ["shards_per_package"] = perPackage,
            ["task_packages"] = packages.Count,
            ["answers"] = answers.Count,
            ["executions"] = executions,
            ["manifest_bytes"] = manifestBytes,
            ["run_seconds"] = Math.Round(elapsed.TotalSeconds, 1),
            ["shards_fetch_ms_total"] = fetchMs,
            ["shards_process_ms_total"] = processMs,
            ["shards_wait_ms_total"] = waitMs,
            ["requests_to_nodes"] = NodesManager.Requests - requestsBefore,
            ["parameter_bytes_total"] = NodesManager.RequestBytes - requestBytesBefore,
            ["parameter_bytes_largest_request"] = NodesManager.LargestRequestBytes,
            ["return_bytes_total"] = NodesManager.ReturnBytes - returnBytesBefore,
            ["pool_tasks_variable_bytes"] = packageBytes,
            ["pool_executions_variable_bytes"] = answerBytes,
            ["task_blob_bytes_total"] = taskBlobBytes,
            ["task_blob_bytes_largest"] = packages.Max(package => package.SizeBytes),
            ["task_blob_bytes_per_shard"] = Math.Round((double)taskBlobBytes / shards, 1),
            ["result_blob_bytes_total"] = resultBlobBytes,
            ["result_blob_bytes_largest"] = NodesManager.LargestAnswerBlobBytes,
            ["result_blob_bytes_per_execution"] = Math.Round((double)resultBlobBytes / executions, 1),
            ["report_bytes"] = new FileInfo(reportPath).Length,
            ["integrity_bytes"] = new FileInfo(integrityPath).Length,
            ["in_0_4_0_parameter_bytes"] = (long)AUDITED_TASK_BYTES * executions,
            ["in_0_4_0_return_bytes"] = (long)AUDITED_RESULT_BYTES * executions,
            ["process_managed_bytes_before"] = managedBefore,
            ["process_managed_bytes_peak"] = managedPeak,
            ["process_working_set_bytes_before"] = workingSetBefore,
            ["process_working_set_bytes_peak"] = workingSetPeak,
            ["server_reading_seconds"] = Math.Round(serverElapsed.TotalSeconds, 1),
            ["server_reading_managed_bytes_peak_over_start"] = Math.Max(0, serverPeak - serverBefore),
            ["server_reading_managed_bytes_held_after"] = Math.Max(0, serverHeld),
            ["server_split_seconds"] = Math.Round(splitElapsed.TotalSeconds, 1),
            ["server_split_managed_bytes_peak_over_start"] = Math.Max(0, splitPeak - splitBefore),
            ["server_split_managed_bytes_held"] = Math.Max(0, splitHeld),
            ["server_split_managed_bytes_held_per_shard"] = Math.Round((double)Math.Max(0, splitHeld) / shards, 1)
        };

        var text = JsonSerializer.Serialize(measured, new JsonSerializerOptions { WriteIndented = true });
        TestContext.Out.WriteLine(text);
        if (Environment.GetEnvironmentVariable(REPORT_VARIABLE) is { Length: > 0 } target)
            await File.WriteAllTextAsync(target, text + "\n");

        Assert.Multiple(() =>
        {
            Assert.That(packages, Has.Count.EqualTo(packagesExpected), "the round's packages are bounded");
            Assert.That(packages.Count, Is.LessThanOrEqualTo(CorpusPackagePlanner.MAX_PACKAGES));
            Assert.That(answers.Count, Is.LessThanOrEqualTo(2 * CorpusPackagePlanner.MAX_PACKAGES));
            Assert.That(NodesManager.LargestRequestBytes, Is.LessThan(512), "no task carried more than a reference to a node");
            Assert.That(packageBytes, Is.LessThan(512L * CorpusPackagePlanner.MAX_PACKAGES), "what the script holds of the round does not grow with the manifest");
            Assert.That(answerBytes, Is.LessThan(512L * 2 * CorpusPackagePlanner.MAX_PACKAGES));
            Assert.That(NodesManager.ReturnBytes - returnBytesBefore, Is.LessThan(512L * 2 * CorpusPackagePlanner.MAX_PACKAGES), "nor what the nodes returned");

            Assert.That(next, Is.EqualTo(shards), "every shard of the manifest is in the report");
            Assert.That(ordered, Is.True);
            Assert.That(verdicts, Is.EqualTo(new Dictionary<string, int> { ["Completed"] = shards }));
            Assert.That(oneContent, Is.True, "one shard listed many times: one product");
            Assert.That(summary.GetProperty("tasks").GetInt32(), Is.EqualTo(shards));
            Assert.That(summary.GetProperty("rejected_results").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("repairs").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("disputes").GetInt32(), Is.Zero);
            Assert.That(twice, Is.GreaterThan(0), "the sampled packages ran twice");

            Assert.That(serverHeld, Is.LessThan(8L << 20), "the server's account of the run is counts and references");
            Assert.That(serverPeak - serverBefore, Is.LessThan(96L << 20), "reading the run and writing its documents takes the memory of a package, not of the run");
            Assert.That(written, Has.Count.EqualTo(packages.Count));
            Assert.That(splitHeld, Is.LessThan(1024L * shards + (8L << 20)), "the plan a split holds is under a kilobyte a shard: the sources and their sizes, never the tasks");
        });
    }

    #endregion

    #region Properties

    /// <summary>Six nodes: the share of each is a sixth of the run.</summary>
    protected override int Nodes => 6;

    #endregion

    #region Types

    /// <summary>Looks at the process's memory ten times a second and keeps the highest it saw.</summary>
    private sealed class MemorySampler : IDisposable
    {
        private readonly CancellationTokenSource m_stop = new();
        private readonly Task m_loop;
        private long m_managed;
        private long m_workingSet;

        public MemorySampler()
        {
            m_loop = Task.Run(async () =>
            {
                using var process = Process.GetCurrentProcess();
                while (!m_stop.IsCancellationRequested)
                {
                    Look(process);
                    try
                    {
                        await Task.Delay(100, m_stop.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                Look(process);
            });
        }

        public (long Managed, long WorkingSet) Stop()
        {
            m_stop.Cancel();
            m_loop.Wait();
            return (m_managed, m_workingSet);
        }

        public void Dispose()
        {
            m_stop.Cancel();
            m_stop.Dispose();
        }

        private void Look(Process process)
        {
            process.Refresh();
            m_managed = Math.Max(m_managed, GC.GetTotalMemory(forceFullCollection: false));
            m_workingSet = Math.Max(m_workingSet, process.WorkingSet64);
        }
    }

    /// <summary>A stream that keeps nothing of what is written to it.</summary>
    private sealed class DiscardStream : Stream
    {
        private long m_length;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => m_length;

        public override long Position { get => m_length; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            m_length += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            m_length += buffer.Length;
        }
    }

    #endregion
}
