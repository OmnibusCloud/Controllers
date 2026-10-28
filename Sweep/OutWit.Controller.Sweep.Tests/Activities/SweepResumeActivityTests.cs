using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Sweep.Model;
using OutWit.Controller.Sweep.Tests.Mock;
using OutWit.Controller.Sweep.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Sweep.Tests.Activities;

/// <summary>
/// Sweep.InitState and Sweep.ChunkCount through the engine, the way a resumed
/// run reaches them: the state variable of the compiled script is set before
/// the run (what an engine resuming a job from its last state does), and the
/// script must continue from it - keep the cursor, loop over the chunks left -
/// or refuse a cursor that does not fit the plan, in words.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SweepResumeActivityTests
{
    #region Constants

    private const string SCRIPT = """
                                  Job:SweepResumeTest(SweepPlan:plan)
                                  {
                                      SweepState:state;
                                      state = Sweep.InitState(plan);
                                      Int:chunks = Sweep.ChunkCount(plan, state);
                                  }
                                  """;

    private const string LEGACY_SCRIPT = """
                                         Job:SweepLegacyCountTest(SweepPlan:plan)
                                         {
                                             SweepState:state = Sweep.InitState(plan);
                                             Int:chunks = Sweep.ChunkCount(plan);
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

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_sweep_resume_{Guid.NewGuid():N}");
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

    /// <summary>A study of seven variants in chunks of three, three and one.</summary>
    private static SweepPlanData Plan()
    {
        return new SweepPlanData
        {
            Options = new SweepOptionsData
            {
                Variants = Enumerable.Range(0, 7).Select(index => new SweepVariantData { VariantIndex = index }).ToList(),
                CalculiX = new SweepCalculiXStudyData { BaseDeckBlobId = Guid.NewGuid() }
            },
            ChunkSizes = [3, 3, 1]
        };
    }

    #endregion

    #region Resume Tests

    [Test]
    public async Task AFreshRunStartsAtTheZeroCursorWithThePlansScheduleTest()
    {
        var job = m_engine.Compile(SCRIPT);

        var status = await m_engine.ScheduleAndWaitAsync(job, Plan());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        var state = job.Variables["state"].Value as SweepStateData;
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.ChunkIndex, Is.Zero);
        Assert.That(state.ChunkSizes, Is.EqualTo(new[] { 3, 3, 1 }), "the zero cursor carries the schedule");
        Assert.That(job.Variables["chunks"].Value, Is.EqualTo(3));
    }

    [Test]
    public async Task AHandedStateIsKeptAndOnlyTheChunksLeftAreCountedTest()
    {
        var job = m_engine.Compile(SCRIPT);
        var manifest = Guid.NewGuid();
        // The fleet was wider at the first run: the handed schedule, not the plan's, is the
        // one the run goes by.
        job.Variables["state"].SetValue(new SweepStateData
        {
            ChunkIndex = 1, NextVariantOrdinal = 6, SucceededCount = 5, FailedCount = 1, ManifestBlobId = manifest, ChunkSizes = [6, 1]
        });

        var status = await m_engine.ScheduleAndWaitAsync(job, Plan());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        var state = job.Variables["state"].Value as SweepStateData;
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.ChunkIndex, Is.EqualTo(1), "the cursor is kept");
        Assert.That(state.ManifestBlobId, Is.EqualTo(manifest));
        Assert.That(state.ChunkSizes, Is.EqualTo(new[] { 6, 1 }));
        Assert.That(job.Variables["chunks"].Value, Is.EqualTo(1), "one chunk left");
    }

    [Test]
    public async Task AStateWithoutAScheduleResumesByThePlansTest()
    {
        var job = m_engine.Compile(SCRIPT);
        job.Variables["state"].SetValue(new SweepStateData
        {
            ChunkIndex = 2, NextVariantOrdinal = 6, SucceededCount = 6, ManifestBlobId = Guid.NewGuid()
        });

        var status = await m_engine.ScheduleAndWaitAsync(job, Plan());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        var state = job.Variables["state"].Value as SweepStateData;
        Assert.That(state!.ChunkSizes, Is.EqualTo(new[] { 3, 3, 1 }), "a 2.1 cursor adopts the plan's schedule");
        Assert.That(job.Variables["chunks"].Value, Is.EqualTo(1));
    }

    [Test]
    public async Task AFullyHarvestedStateLeavesNothingToRunTest()
    {
        var job = m_engine.Compile(SCRIPT);
        job.Variables["state"].SetValue(new SweepStateData
        {
            ChunkIndex = 3, NextVariantOrdinal = 7, SucceededCount = 7, ManifestBlobId = Guid.NewGuid(), ChunkSizes = [3, 3, 1]
        });

        var status = await m_engine.ScheduleAndWaitAsync(job, Plan());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        Assert.That(job.Variables["chunks"].Value, Is.Zero);
    }

    [Test]
    public async Task AStateThatDoesNotFitThePlanIsRefusedInWordsTest()
    {
        var job = m_engine.Compile(SCRIPT);
        job.Variables["state"].SetValue(new SweepStateData
        {
            ChunkIndex = 1, NextVariantOrdinal = 4, SucceededCount = 4, ManifestBlobId = Guid.NewGuid(), ChunkSizes = [4, 4]
        });

        var status = await m_engine.ScheduleAndWaitAsync(job, Plan());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(status.Message, Does.Contain("cannot resume").And.Contain("covers 8 variant(s), the study has 7"));
    }

    [Test]
    public async Task AScriptPassingThePlanAloneStillCountsEveryChunkTest()
    {
        var job = m_engine.Compile(LEGACY_SCRIPT);

        var status = await m_engine.ScheduleAndWaitAsync(job, Plan());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        Assert.That(job.Variables["chunks"].Value, Is.EqualTo(3));
    }

    #endregion
}
