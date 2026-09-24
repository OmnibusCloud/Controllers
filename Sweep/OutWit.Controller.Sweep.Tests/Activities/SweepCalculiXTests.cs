using System.Text;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.CalculiX.Runtime;
using OutWit.Controller.Sweep.Model;
using OutWit.Controller.Sweep.Tests.Mock;
using OutWit.Controller.Sweep.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Sweep.Tests.Activities;

/// <summary>
/// The CalculiX family through the engine: the bundled SweepCalculiX.wit
/// (Loop{Grid.ForEach}, blob transport, mock nodes, the fake solver) must
/// carry a chunked study end to end - substituted decks out, manifest rows
/// back, a failing variant recorded as a row rather than a failed job - and
/// the plan must refuse a malformed study before any node sees a task.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SweepCalculiXTests
{
    #region Constants

    private const string DECK_TEMPLATE = "*HEADING\nvariant {{oc1}}\n*STEP\n*STATIC\n*END STEP\n";

    #endregion

    #region Fields

    private string m_blobStoragePath = null!;
    private SweepTestBlobService m_blobService = null!;
    private string m_script = null!;
    private IWitEngine m_engine = null!;
    private string? m_previousSolverPath;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var solutionRoot = SweepTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var scriptPath = SweepTestPaths.GetScriptPath(solutionRoot, "SweepCalculiX");
        if (!File.Exists(scriptPath))
            Assert.Ignore($"SweepCalculiX.wit not found at {scriptPath}");

        m_script = File.ReadAllText(scriptPath);

        var controllersPath = SweepTestPaths.FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        var fakeCcxPath = SweepTestPaths.FindFakeCcxPath(solutionRoot);
        if (fakeCcxPath == null)
            Assert.Ignore("fake-ccx not built");

        m_previousSolverPath = Environment.GetEnvironmentVariable(CcxBinaryResolver.ENV_SOLVER_PATH);
        Environment.SetEnvironmentVariable(CcxBinaryResolver.ENV_SOLVER_PATH, fakeCcxPath);

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_sweep_ccx_{Guid.NewGuid():N}");
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
        Environment.SetEnvironmentVariable(CcxBinaryResolver.ENV_SOLVER_PATH, m_previousSolverPath);

        if (m_blobStoragePath != null && Directory.Exists(m_blobStoragePath))
            Directory.Delete(m_blobStoragePath, recursive: true);
    }

    #endregion

    #region Tools

    private SweepOptionsData TemplateStudy(IReadOnlyList<string> values, Guid baseDeck, int firstChunk = 2, int maxChunk = 3)
    {
        return new SweepOptionsData
        {
            Parameters = [new SweepParameterData { Name = "E-modulus", Token = "{{oc1}}" }],
            Variants = values
                .Select((value, index) => new SweepVariantData { VariantIndex = index, Values = [value] })
                .ToList(),
            FirstChunkSize = firstChunk,
            MaxChunkSize = maxChunk,
            CalculiX = new SweepCalculiXStudyData { BaseDeckBlobId = baseDeck, NodeCount = 100, ElementCount = 100, Threads = 1 }
        };
    }

    private async Task<SweepManifestData> ManifestAsync(SweepStateData state)
    {
        Assert.That(state.ManifestBlobId, Is.Not.Null);
        var manifest = MemoryPackSerializer.Deserialize<SweepManifestData>(
            await File.ReadAllBytesAsync(m_blobService.GetStoredPath(state.ManifestBlobId ?? Guid.Empty)));
        Assert.That(manifest, Is.Not.Null);
        return manifest ?? new SweepManifestData();
    }

    #endregion

    #region Script Tests

    [Test]
    public void TheBundledScriptCompilesTest()
    {
        var job = m_engine.Compile(m_script);

        Assert.That(job, Is.Not.Null);
        Assert.That(job.Activities.Count, Is.GreaterThan(0));

        // A silently-wrong compile (missing statements) must not pass as "compiles".
        var variables = job.Variables.Select(variable => variable.Name).ToList();
        Assert.That(variables, Is.SupersetOf(new[] { "opts", "plan", "state", "chunks", "tasks", "wave", "manifest" }));
    }

    #endregion

    #region Study Tests

    [Test]
    public async Task AChunkedTemplateStudyRunsEndToEndTest()
    {
        var deckBlobId = m_blobService.AddText(DECK_TEMPLATE, "base.inp");

        // Seven variants over one parameter; #3 asks the fake solver to fail.
        var values = new[] { "v0", "v1", "v2", "FAKE-FAIL", "v4", "v5", "v6" };

        var job = m_engine.Compile(m_script);
        var status = await m_engine.ScheduleAndWaitAsync(job, TemplateStudy(values, deckBlobId));

        // One diverged variant must NOT fail the sweep - it is a red row.
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var plan = job.Variables["plan"].Value as SweepPlanData;
        Assert.That(plan, Is.Not.Null);
        // The client asked for chunks 2..3; the test host offers three mock nodes, so the plan
        // opens as wide as the fleet: three, three, and the last one.
        Assert.That(plan?.ChunkSizes, Is.EqualTo(new[] { 3, 3, 1 }));

        var state = job.Variables["state"].Value as SweepStateData ?? new SweepStateData();
        Assert.That(state.ChunkIndex, Is.EqualTo(3));
        Assert.That(state.SucceededCount, Is.EqualTo(6));
        Assert.That(state.FailedCount, Is.EqualTo(1));
        Assert.That(state.RefusedCount, Is.Zero);
        Assert.That(job.Variables["manifest"].Value, Is.EqualTo(state.ManifestBlobId), "the script returns the manifest blob");

        var manifest = await ManifestAsync(state);
        Assert.That(manifest.Rows.Select(row => row.VariantIndex), Is.EqualTo(Enumerable.Range(0, values.Length)), "rows sorted by variant");
        Assert.That(manifest.Rows.All(row => row.CalculiX != null && row.OpenFOAM == null), Is.True, "every row carries the CalculiX result, verbatim");

        var failed = manifest.Rows.Single(row => row.VariantIndex == 3);
        Assert.That(failed.Outcome, Is.EqualTo(SweepOutcome.Failed));
        Assert.That(failed.CalculiX?.ExitCode, Is.EqualTo(201));
        Assert.That(failed.CalculiX?.LogTail, Does.Contain("fake failure"));
        Assert.That(failed.CalculiX?.FrdBlobId, Is.Null);

        foreach (var row in manifest.Rows.Where(row => row.VariantIndex != 3))
        {
            Assert.That(row.Outcome, Is.EqualTo(SweepOutcome.Succeeded), $"variant #{row.VariantIndex}");
            Assert.That(row.CalculiX?.SolveSeconds, Is.GreaterThan(0));
            Assert.That(row.CalculiX?.FrdBlobId, Is.Not.Null);
            // The fake solver leaves a zero-byte .dat, like real ccx on a deck
            // without *NODE PRINT requests - an empty artifact yields no blob.
            Assert.That(row.CalculiX?.DatBlobId, Is.Null);
        }

        // The document-client view: the index mirrors the manifest, sorted by
        // variant, labelled by the parameter values, with the artifacts by kind.
        Assert.That(state.Results.Select(entry => entry.VariantIndex), Is.EqualTo(Enumerable.Range(0, values.Length)));
        Assert.That(state.Results.Select(entry => entry.Label), Is.EqualTo(values.Select(value => $"E-modulus={value}")));
        Assert.That(state.Results.Single(entry => entry.VariantIndex == 3).Artifacts, Is.Empty);
        foreach (var entry in state.Results.Where(entry => entry.VariantIndex != 3))
        {
            var row = manifest.Rows.Single(candidate => candidate.VariantIndex == entry.VariantIndex);
            Assert.That(entry.Outcome, Is.EqualTo(SweepOutcome.Succeeded));
            Assert.That(entry.Artifacts.Select(artifact => artifact.Kind), Is.EqualTo(new[] { SweepArtifactKind.CalculiXFrd }));
            Assert.That(entry.Artifacts[0].BlobId, Is.EqualTo(row.CalculiX?.FrdBlobId));
        }

        // Substitution proven through the whole pipeline: the fake solver
        // echoes the deck into the .frd.
        var echoed = manifest.Rows.Single(row => row.VariantIndex == 4);
        var frdText = await File.ReadAllTextAsync(m_blobService.GetStoredPath(echoed.CalculiX?.FrdBlobId ?? Guid.Empty));
        Assert.That(frdText, Does.Contain("variant v4"));
        Assert.That(frdText, Does.Not.Contain("{{"));
    }

    [Test]
    public async Task ADeckSetStudyRunsEndToEndTest()
    {
        // Three ready decks (meshed elsewhere, no tokens, no parameters); the
        // middle one fails on purpose - a bad deck in the set is a red row.
        var blobA = m_blobService.AddText("*HEADING\ndeck A mesh-coarse\n*STEP\n*STATIC\n*END STEP\n", "coarse.inp");
        var blobB = m_blobService.AddText("*HEADING\ndeck B FAKE-FAIL\n*STEP\n*STATIC\n*END STEP\n", "broken.inp");
        var blobC = m_blobService.AddText("*HEADING\ndeck C mesh-fine\n*STEP\n*STATIC\n*END STEP\n", "fine.inp");

        var options = new SweepOptionsData
        {
            Variants = [new SweepVariantData { VariantIndex = 0 }, new SweepVariantData { VariantIndex = 1 }, new SweepVariantData { VariantIndex = 2 }],
            FirstChunkSize = 2,
            MaxChunkSize = 3,
            CalculiX = new SweepCalculiXStudyData
            {
                Decks =
                [
                    new SweepCalculiXDeckData { VariantIndex = 0, DeckBlobId = blobA, NodeCount = 1_000, ElementCount = 700 },
                    new SweepCalculiXDeckData { VariantIndex = 1, DeckBlobId = blobB, NodeCount = 8_000, ElementCount = 6_000 },
                    new SweepCalculiXDeckData { VariantIndex = 2, DeckBlobId = blobC, NodeCount = 27_000, ElementCount = 21_000 }
                ],
                Threads = 1
            }
        };

        var inpCountBefore = Directory.GetFiles(m_blobStoragePath, "*.inp").Length;

        var job = m_engine.Compile(m_script);
        var status = await m_engine.ScheduleAndWaitAsync(job, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var state = job.Variables["state"].Value as SweepStateData ?? new SweepStateData();
        Assert.That(state.SucceededCount, Is.EqualTo(2));
        Assert.That(state.FailedCount, Is.EqualTo(1));
        Assert.That(state.Results.Select(entry => entry.Label), Is.All.Empty, "a deck-set variant has no parameter values to be named by");

        var manifest = await ManifestAsync(state);
        var fine = manifest.Rows.Single(row => row.VariantIndex == 2);
        Assert.That(fine.Outcome, Is.EqualTo(SweepOutcome.Succeeded));
        var frdText = await File.ReadAllTextAsync(m_blobService.GetStoredPath(fine.CalculiX?.FrdBlobId ?? Guid.Empty));
        Assert.That(frdText, Does.Contain("deck C mesh-fine"), "the node solved the client's deck verbatim");
        Assert.That(manifest.Rows.Single(row => row.VariantIndex == 1).Outcome, Is.EqualTo(SweepOutcome.Failed));

        // Deck-set chunks are pure metadata: nothing instantiated, nothing re-uploaded.
        Assert.That(Directory.GetFiles(m_blobStoragePath, "*.inp").Length, Is.EqualTo(inpCountBefore));
    }

    #endregion

    #region Refusal Tests

    [Test]
    public async Task AStudyMixingATemplateAndADeckSetIsRefusedUpFrontTest()
    {
        var blob = m_blobService.AddText("*HEADING\nmixed probe\n", "probe.inp");
        var options = new SweepOptionsData
        {
            Variants = [new SweepVariantData { VariantIndex = 0 }, new SweepVariantData { VariantIndex = 1 }],
            CalculiX = new SweepCalculiXStudyData
            {
                BaseDeckBlobId = blob,
                Decks = [new SweepCalculiXDeckData { VariantIndex = 0, DeckBlobId = blob }]
            }
        };

        var status = await m_engine.ScheduleAndWaitAsync(m_engine.Compile(m_script), options);

        Assert.That(status.Result, Is.Not.EqualTo(WitProcessingResult.Completed));
        Assert.That(status.Message, Does.Contain("either a deck set or a template, never both"));
    }

    [Test]
    public async Task ADuplicateVariantIndexIsRefusedUpFrontTest()
    {
        var blob = m_blobService.AddText("*HEADING\ndup probe {{oc1}}\n", "dup.inp");
        var options = TemplateStudy(["1", "2", "3"], blob);
        options.Variants[1].VariantIndex = 0;

        var status = await m_engine.ScheduleAndWaitAsync(m_engine.Compile(m_script), options);

        Assert.That(status.Result, Is.Not.EqualTo(WitProcessingResult.Completed));
        Assert.That(status.Message, Does.Contain("Variant index 0 appears 2 times"));
    }

    [Test]
    public async Task AStudyWithoutAFamilyBlockIsRefusedUpFrontTest()
    {
        var options = new SweepOptionsData { Variants = [new SweepVariantData { VariantIndex = 0 }] };

        var status = await m_engine.ScheduleAndWaitAsync(m_engine.Compile(m_script), options);

        Assert.That(status.Result, Is.Not.EqualTo(WitProcessingResult.Completed));
        Assert.That(status.Message, Does.Contain("The study carries no family block"));
    }

    #endregion
}
