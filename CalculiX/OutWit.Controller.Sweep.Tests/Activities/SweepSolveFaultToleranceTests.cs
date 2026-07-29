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
/// Fault gate: a node that dies mid-chunk must cost nothing but a retry —
/// Grid re-packs the victim's tasks onto the survivors, and because Ccx.Solve
/// is a pure function over immutable blobs, the sweep completes with a full
/// manifest and the injected fault never surfaces as a failed variant.
/// </summary>
[TestFixture]
public class SweepSolveFaultToleranceTests
{
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

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_sweep_faulttest_{Guid.NewGuid():N}");
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
        Environment.SetEnvironmentVariable(CcxBinaryResolver.ENV_SOLVER_PATH, null);

        if (Directory.Exists(m_blobStoragePath))
            Directory.Delete(m_blobStoragePath, recursive: true);
    }

    #endregion

    #region Fault Tests

    [Test]
    public async Task NodeDeathMidChunkCostsARetryNotAVariantTest()
    {
        var deck = "*HEADING\nvariant {{oc1}}\n*STEP\n*STATIC\n*END STEP\n";
        var deckBlobId = await m_blobService.UploadBytesAsync(Encoding.UTF8.GetBytes(deck), "base.inp");

        // First chunk of 3 over 3 nodes: the allocator hands one task to each,
        // so the victim (first, equal-rate order is stable) is guaranteed to
        // participate — and to die on its first batch.
        var values = new[] { "v0", "v1", "v2", "FAKE-FAIL", "v4", "v5", "v6" };
        var options = new SweepOptionsData
        {
            Parameters = [new SweepParameterData { Name = "P", Token = "{{oc1}}" }],
            Variants = values
                .Select((value, index) => new SweepVariantData { VariantIndex = index, Values = [value] })
                .ToList(),
            Threads = 1,
            NodeCount = 100,
            ElementCount = 100,
            FirstChunkSize = 3,
            MaxChunkSize = 4
        };

        var job = m_engine.Compile(m_sweepScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, deckBlobId, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var state = job.Variables["state"].Value as SweepStateData;
        Assert.That(state, Is.Not.Null);

        var manifest = MemoryPackSerializer.Deserialize<SweepManifestData>(
            await File.ReadAllBytesAsync(m_blobService.GetStoredPath(state!.ManifestBlobId!.Value)));

        // The full study is harvested; the ONLY failed row is the deck the
        // fake solver rejects — the injected node death left no trace.
        Assert.That(manifest!.Rows.Select(row => row.VariantIndex), Is.EquivalentTo(Enumerable.Range(0, values.Length)));
        Assert.That(state.CompletedCount, Is.EqualTo(6));
        Assert.That(state.FailedCount, Is.EqualTo(1));
        Assert.That(manifest.Rows.Single(row => !row.Succeeded).VariantIndex, Is.EqualTo(3));
    }

    #endregion
}
