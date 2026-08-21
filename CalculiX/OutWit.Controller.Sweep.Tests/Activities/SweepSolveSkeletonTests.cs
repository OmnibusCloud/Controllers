using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.CalculiX.Model;
using OutWit.Controller.CalculiX.Runtime;
using OutWit.Controller.Sweep.Tests.Mock;
using OutWit.Controller.Sweep.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;
using System.Text;

namespace OutWit.Controller.Sweep.Tests.Activities;

/// <summary>
/// Distributed gates: the bundled SweepSolve.wit running through the engine
/// (Loop{Grid.ForEach}, blob transport, mock nodes, fake solver) must carry a
/// chunked study end to end — substituted decks out, manifest rows back, a
/// failing variant recorded as data rather than a failed job.
/// </summary>
[TestFixture]
public class SweepSolveSkeletonTests
{
    #region Constants

    private const string DECK_TEMPLATE = "*HEADING\nvariant {{oc1}}\n*STEP\n*STATIC\n*END STEP\n";

    #endregion

    #region Fields

    private string m_blobStoragePath = null!;
    private SweepTestBlobService m_blobService = null!;
    private string m_sweepScript = null!;
    private IWitEngine m_engine = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var solutionRoot = SweepTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var scriptPath = SweepTestPaths.GetSweepSolveScriptPath(solutionRoot);
        if (!File.Exists(scriptPath))
            Assert.Ignore($"SweepSolve.wit not found at {scriptPath}");

        m_sweepScript = File.ReadAllText(scriptPath);

        var controllersPath = SweepTestPaths.FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        var fakeCcxPath = SweepTestPaths.FindFakeCcxPath(solutionRoot);
        if (fakeCcxPath == null)
            Assert.Ignore("fake-ccx not built");

        Environment.SetEnvironmentVariable(CcxBinaryResolver.ENV_SOLVER_PATH, fakeCcxPath);

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_sweep_blobtest_{Guid.NewGuid():N}");
        m_blobService = new SweepTestBlobService(m_blobStoragePath);

        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
            moduleFolder: controllersPath,
            configureServices: services => services.AddSingleton<IWitBlobService>(m_blobService));

        m_engine = WitEngineSdk.Instance;
        m_engine.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new SweepTestNodesManager(WitEngineNodeSdk.Instance));
            });
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(CcxBinaryResolver.ENV_SOLVER_PATH, null);

        if (Directory.Exists(m_blobStoragePath))
            Directory.Delete(m_blobStoragePath, recursive: true);
    }

    #endregion

    #region Script Tests

    [Test]
    public void BundledScriptCompilesTest()
    {
        var job = m_engine.Compile(m_sweepScript);

        Assert.That(job, Is.Not.Null);
        Assert.That(job.Activities.Count, Is.GreaterThan(0));

        // A silently-wrong compile (missing statements) must not pass as "compiles".
        var variables = job.Variables.Select(variable => variable.Name).ToList();
        Assert.That(variables, Is.SupersetOf(new[] { "baseDeck", "opts", "plan", "state", "chunks", "tasks", "wave", "responses" }));
    }

    [Test]
    public async Task ChunkedSweepRunsEndToEndTest()
    {
        var deckBlobId = await m_blobService.UploadBytesAsync(Encoding.UTF8.GetBytes(DECK_TEMPLATE), "base.inp");

        // Seven variants over one parameter; #3 asks the fake solver to fail.
        // First 2 / cap 3 makes the schedule [2, 3, 2] — three chunks, so the
        // manifest accumulation and the state reassignment both run more than
        // once, and the failing variant lands mid-chunk.
        var values = new[] { "v0", "v1", "v2", "FAKE-FAIL", "v4", "v5", "v6" };
        var options = new SweepOptionsData
        {
            Parameters = [new SweepParameterData { Name = "E-modulus", Token = "{{oc1}}" }],
            Variants = values
                .Select((value, index) => new SweepVariantData { VariantIndex = index, Values = [value] })
                .ToList(),
            Threads = 1,
            NodeCount = 100,
            ElementCount = 100,
            FirstChunkSize = 2,
            MaxChunkSize = 3
        };

        var job = m_engine.Compile(m_sweepScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, deckBlobId, options);

        // One diverged variant must NOT fail the sweep — it is a red row.
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var plan = job.Variables["plan"].Value as SweepPlanData;
        Assert.That(plan, Is.Not.Null);
        Assert.That(plan!.ChunkSizes, Is.EqualTo(new[] { 2, 3, 2 }));

        var state = job.Variables["state"].Value as SweepStateData;
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.ChunkIndex, Is.EqualTo(plan.ChunkSizes.Count));
        Assert.That(state.CompletedCount, Is.EqualTo(6));
        Assert.That(state.FailedCount, Is.EqualTo(1));

        var manifestBlobId = state.ManifestBlobId;
        Assert.That(manifestBlobId, Is.Not.Null);
        Assert.That(job.Variables["responses"].Value, Is.EqualTo(manifestBlobId.Value));

        var manifest = MemoryPackSerializer.Deserialize<SweepManifestData>(
            await File.ReadAllBytesAsync(m_blobService.GetStoredPath(manifestBlobId.Value)));
        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.Rows.Select(row => row.VariantIndex), Is.EquivalentTo(Enumerable.Range(0, values.Length)));

        // The document-client view: the state's result index mirrors the
        // manifest, sorted by variant, with the same artifact ids.
        Assert.That(state.Results.Select(entry => entry.VariantIndex),
            Is.EqualTo(Enumerable.Range(0, values.Length)), "the index is cumulative and sorted");
        Assert.That(state.Results.Single(entry => entry.VariantIndex == 3).Succeeded, Is.False);
        Assert.That(state.Results.Single(entry => entry.VariantIndex == 3).FrdBlobId, Is.Null);
        foreach (var entry in state.Results.Where(entry => entry.VariantIndex != 3))
        {
            var row = manifest.Rows.Single(candidate => candidate.VariantIndex == entry.VariantIndex);
            Assert.That(entry.Succeeded, Is.True, $"variant #{entry.VariantIndex}");
            Assert.That(entry.FrdBlobId, Is.EqualTo(row.FrdBlobId), $"variant #{entry.VariantIndex}");
        }

        var failed = manifest.Rows.Single(row => row.VariantIndex == 3);
        Assert.That(failed.Succeeded, Is.False);
        Assert.That(failed.ExitCode, Is.EqualTo(201));
        Assert.That(failed.LogTail, Does.Contain("fake failure"));
        Assert.That(failed.FrdBlobId, Is.Null);

        foreach (var row in manifest.Rows.Where(row => row.VariantIndex != 3))
        {
            Assert.That(row.Succeeded, Is.True, $"variant #{row.VariantIndex}");
            Assert.That(row.ExitCode, Is.Zero);
            Assert.That(row.SolveSeconds, Is.GreaterThan(0));
            Assert.That(row.FrdBlobId, Is.Not.Null);

            // The fake solver leaves a zero-byte .dat, like real ccx on a deck
            // without *NODE PRINT requests — an empty artifact must yield NO
            // blob rather than an upload of empty data.
            Assert.That(row.DatBlobId, Is.Null);
        }

        // Substitution proven through the whole pipeline: the fake solver
        // echoes the deck into the .frd, so the artifact of variant #4 must
        // carry its substituted value and no surviving placeholder.
        var echoed = manifest.Rows.Single(row => row.VariantIndex == 4);
        var frdText = await File.ReadAllTextAsync(m_blobService.GetStoredPath(echoed.FrdBlobId!.Value));
        Assert.That(frdText, Does.Contain("variant v4"));
        Assert.That(frdText, Does.Not.Contain("{{"));
    }

    [Test]
    public async Task DeckSetSweepRunsEndToEndTest()
    {
        // Three ready decks (UC-3: meshed elsewhere, no tokens, no
        // parameters); the middle one fails on purpose — a bad deck in the
        // set is a red row, not a failed study.
        const string deckA = "*HEADING\ndeck A mesh-coarse\n*STEP\n*STATIC\n*END STEP\n";
        const string deckB = "*HEADING\ndeck B FAKE-FAIL\n*STEP\n*STATIC\n*END STEP\n";
        const string deckC = "*HEADING\ndeck C mesh-fine\n*STEP\n*STATIC\n*END STEP\n";

        var blobA = await m_blobService.UploadBytesAsync(Encoding.UTF8.GetBytes(deckA), "coarse.inp");
        var blobB = await m_blobService.UploadBytesAsync(Encoding.UTF8.GetBytes(deckB), "broken.inp");
        var blobC = await m_blobService.UploadBytesAsync(Encoding.UTF8.GetBytes(deckC), "fine.inp");

        var options = new SweepOptionsData
        {
            Variants =
            [
                new SweepVariantData { VariantIndex = 0, DeckBlobId = blobA, NodeCount = 1_000, ElementCount = 700 },
                new SweepVariantData { VariantIndex = 1, DeckBlobId = blobB, NodeCount = 8_000, ElementCount = 6_000 },
                new SweepVariantData { VariantIndex = 2, DeckBlobId = blobC, NodeCount = 27_000, ElementCount = 21_000 }
            ],
            Threads = 1,
            FirstChunkSize = 2,
            MaxChunkSize = 3
        };

        var inpCountBefore = Directory.GetFiles(m_blobStoragePath, "*.inp").Length;

        // The base-deck slot is the script's signature, not the study's
        // content — any of the set's blobs serves; the plan must not read it.
        var job = m_engine.Compile(m_sweepScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, blobA, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var state = job.Variables["state"].Value as SweepStateData;
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.CompletedCount, Is.EqualTo(2));
        Assert.That(state.FailedCount, Is.EqualTo(1));

        var manifest = MemoryPackSerializer.Deserialize<SweepManifestData>(
            await File.ReadAllBytesAsync(m_blobService.GetStoredPath(state.ManifestBlobId!.Value)));
        Assert.That(manifest!.Rows.Select(row => row.VariantIndex), Is.EquivalentTo(new[] { 0, 1, 2 }));

        // The node solved the client's deck verbatim — the fake solver
        // echoes the deck text into the .frd.
        var fine = manifest.Rows.Single(row => row.VariantIndex == 2);
        Assert.That(fine.Succeeded, Is.True);
        var frdText = await File.ReadAllTextAsync(m_blobService.GetStoredPath(fine.FrdBlobId!.Value));
        Assert.That(frdText, Does.Contain("deck C mesh-fine"));

        var failed = manifest.Rows.Single(row => row.VariantIndex == 1);
        Assert.That(failed.Succeeded, Is.False);
        Assert.That(failed.ExitCode, Is.EqualTo(201));

        // Deck-set chunks are pure metadata: nothing instantiated, nothing
        // re-uploaded — the decks the client uploaded are the decks solved.
        Assert.That(Directory.GetFiles(m_blobStoragePath, "*.inp").Length, Is.EqualTo(inpCountBefore));
    }

    [Test]
    public async Task MixedStudyIsRejectedUpFrontTest()
    {
        var blob = await m_blobService.UploadBytesAsync(
            Encoding.UTF8.GetBytes("*HEADING\nmixed probe\n"), "probe.inp");

        var options = new SweepOptionsData
        {
            Variants =
            [
                new SweepVariantData { VariantIndex = 0, DeckBlobId = blob },
                new SweepVariantData { VariantIndex = 1 }
            ],
            FirstChunkSize = 2,
            MaxChunkSize = 3
        };

        // Half a deck set, half a template: unsolvable by construction — the
        // plan must reject the study before any node sees a task.
        var job = m_engine.Compile(m_sweepScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, blob, options);

        Assert.That(status.Result, Is.Not.EqualTo(WitProcessingResult.Completed));
    }

    [Test]
    public async Task DuplicateVariantIndexIsRejectedUpFrontTest()
    {
        var blob = await m_blobService.UploadBytesAsync(
            Encoding.UTF8.GetBytes("*HEADING\ndup probe {{oc1}}\n"), "dup.inp");

        var options = new SweepOptionsData
        {
            Parameters = [new SweepParameterData { Name = "E", Token = "{{oc1}}" }],
            Variants =
            [
                new SweepVariantData { VariantIndex = 0, Values = ["1"] },
                new SweepVariantData { VariantIndex = 0, Values = ["2"] },
                new SweepVariantData { VariantIndex = 1, Values = ["3"] }
            ],
            FirstChunkSize = 2,
            MaxChunkSize = 3
        };

        // The manifest maps results by VariantIndex, never positionally — a
        // repeated index would burn the fleet's time and come back
        // permanently unmappable, so the plan rejects the study before any
        // node sees a task.
        var job = m_engine.Compile(m_sweepScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, blob, options);

        Assert.That(status.Result, Is.Not.EqualTo(WitProcessingResult.Completed));
    }

    #endregion
}
