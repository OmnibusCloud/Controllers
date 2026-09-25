using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.CalculiX.Model;
using OutWit.Controller.Sweep.Model;
using OutWit.Controller.Sweep.Tests.Mock;
using OutWit.Controller.Sweep.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Sweep.Tests.Activities;

/// <summary>
/// Sweep.Harvest through the engine, fed a wave directly: the chunk's
/// variants come back, each once, or the harvest fails loudly naming what is
/// missing, repeated or foreign - never a manifest that miscounts.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SweepHarvestTests
{
    #region Constants

    private const string SCRIPT = """
                                  Job:SweepHarvestTest(SweepPlan:plan, SweepState:state, CcxResultCollection:wave)
                                  {
                                      SweepState:next = Sweep.Harvest(plan, state, wave);
                                  }
                                  """;

    #endregion

    #region Fields

    private string m_blobStoragePath = null!;
    private SweepTestBlobService m_blobService = null!;
    private IWitEngine m_engine = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var controllersPath = SweepTestPaths.FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_sweep_harvest_{Guid.NewGuid():N}");
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
        if (m_blobStoragePath != null && Directory.Exists(m_blobStoragePath))
            Directory.Delete(m_blobStoragePath, recursive: true);
    }

    #endregion

    #region Tools

    /// <summary>
    /// A study of four variants whose indices are not their positions
    /// (10, 11, 12, 13), in two chunks of two.
    /// </summary>
    private static SweepPlanData Plan()
    {
        return new SweepPlanData
        {
            Options = new SweepOptionsData
            {
                Variants = new[] { 10, 11, 12, 13 }.Select(index => new SweepVariantData { VariantIndex = index }).ToList(),
                CalculiX = new SweepCalculiXStudyData { BaseDeckBlobId = Guid.NewGuid() }
            },
            ChunkSizes = [2, 2]
        };
    }

    private static SweepStateData SecondChunk()
    {
        return new SweepStateData { ChunkIndex = 1, NextVariantOrdinal = 2 };
    }

    private static List<CcxResultData?> Wave(params int[] variantIndices)
    {
        return variantIndices.Select(index => (CcxResultData?)new CcxResultData { VariantIndex = index }).ToList();
    }

    private async Task<IWitProcessingStatus> HarvestAsync(IWitJob job, params int[] variantIndices)
    {
        return await m_engine.ScheduleAndWaitAsync(job, Plan(), SecondChunk(), Wave(variantIndices));
    }

    #endregion

    #region Harvest Tests

    [Test]
    public async Task TheChunksVariantsInCompletionOrderAreHarvestedTest()
    {
        var job = m_engine.Compile(SCRIPT);

        var status = await HarvestAsync(job, 13, 12);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        var next = job.Variables["next"].Value as SweepStateData;
        Assert.That(next, Is.Not.Null);
        Assert.That(next!.ChunkIndex, Is.EqualTo(2));
        Assert.That(next.NextVariantOrdinal, Is.EqualTo(4));
        Assert.That(next.SucceededCount, Is.EqualTo(2));
        Assert.That(next.Results.Select(entry => entry.VariantIndex), Is.EqualTo(new[] { 12, 13 }));
    }

    [Test]
    public async Task AShortWaveFailsNamingTheMissingVariantTest()
    {
        var status = await HarvestAsync(m_engine.Compile(SCRIPT), 13);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(status.Message, Does.Contain("returned 1 CalculiX result(s) for 2 task(s)").And.Contain("missing variant(s) #12"));
    }

    [Test]
    public async Task AVariantTwiceFailsEvenWhenTheCountMatchesTest()
    {
        var status = await HarvestAsync(m_engine.Compile(SCRIPT), 12, 12);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(status.Message, Does.Contain("missing variant(s) #13").And.Contain("variant(s) #12 more than once"));
    }

    [Test]
    public async Task AVariantFromAnotherChunkFailsTest()
    {
        // Variant 10 belongs to the first chunk: harvested again here, it
        // would be counted twice in the manifest.
        var status = await HarvestAsync(m_engine.Compile(SCRIPT), 12, 10);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(status.Message, Does.Contain("missing variant(s) #13").And.Contain("variant(s) #10 not in the chunk"));
    }

    #endregion
}
