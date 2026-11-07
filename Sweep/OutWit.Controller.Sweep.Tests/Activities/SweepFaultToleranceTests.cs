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
/// Fault gate: a node that dies mid-chunk must cost nothing but a retry -
/// Grid re-packs the victim's tasks onto the survivors, and because a node
/// activity is a pure function over immutable blobs, the sweep completes with
/// a full manifest and the injected fault never surfaces as a failed variant.
/// Driven through the CalculiX family; the orchestration it proves is the
/// same for every family.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SweepFaultToleranceTests
{
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

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_sweep_fault_{Guid.NewGuid():N}");
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
                services.AddSingleton<IWitNodesManager>(new FaultInjectionNodesManager(WitEngineNodeSdk.Instance));
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

    #region Fault Tests

    [Test]
    public async Task NodeDeathMidChunkCostsARetryNotAVariantTest()
    {
        var deckBlobId = m_blobService.AddText("*HEADING\nvariant {{oc1}}\n*STEP\n*STATIC\n*END STEP\n", "base.inp");

        // First chunk of 3 over 3 nodes: the allocator hands one task to each,
        // so the victim (first, equal-rate order is stable) is guaranteed to
        // participate - and to die on its first batch.
        var values = new[] { "v0", "v1", "v2", "FAKE-FAIL", "v4", "v5", "v6" };
        var options = new SweepOptionsData
        {
            Parameters = [new SweepParameterData { Name = "P", Token = "{{oc1}}" }],
            Variants = values
                .Select((value, index) => new SweepVariantData { VariantIndex = index, Values = [value] })
                .ToList(),
            FirstChunkSize = 3,
            MaxChunkSize = 4,
            CalculiX = new SweepCalculiXStudyData { BaseDeckBlobId = deckBlobId, NodeCount = 100, ElementCount = 100, Threads = 1 }
        };

        var job = m_engine.Compile(m_script);
        var status = await m_engine.ScheduleAndWaitAsync(job, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var state = job.Variables["state"].Value as SweepStateData ?? new SweepStateData();
        var manifest = MemoryPackSerializer.Deserialize<SweepManifestData>(
            await File.ReadAllBytesAsync(m_blobService.GetStoredPath(state.ManifestBlobId ?? Guid.Empty))) ?? new SweepManifestData();

        // The full study is harvested; the ONLY failed row is the deck the
        // fake solver rejects - the injected node death left no trace.
        Assert.That(manifest.Rows.Select(row => row.VariantIndex), Is.EqualTo(Enumerable.Range(0, values.Length)));
        Assert.That(state.SucceededCount, Is.EqualTo(6));
        Assert.That(state.FailedCount, Is.EqualTo(1));
        Assert.That(manifest.Rows.Single(row => row.Outcome != SweepOutcome.Succeeded).VariantIndex, Is.EqualTo(3));
    }

    #endregion
}
