using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Sweep.Model;
using OutWit.Controller.Sweep.Tests.Mock;
using OutWit.Controller.Sweep.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Sweep.Tests.Activities;

/// <summary>
/// Sweep.InitState and Sweep.ChunkCount through the engine, the way a resumed
/// run reaches them: the state (and schedule) variables of the compiled
/// script are set before the run (what an engine resuming a job from its
/// last state does), and the script must continue from them - keep the
/// cursor, go by the handed schedule, loop over the chunks left - or refuse
/// a cursor that does not fit the plan, in words. A fresh run writes the
/// plan's schedule into the schedule variable and leaves the state's layout
/// alone.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SweepResumeActivityTests
{
    #region Constants

    private const string SCRIPT = """
                                  Job:SweepResumeTest(SweepPlan:plan)
                                  {
                                      SweepSchedule:schedule;
                                      SweepState:state;
                                      state = Sweep.InitState(plan, schedule);
                                      Int:chunks = Sweep.ChunkCount(plan, state, schedule);
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

    private static SweepScheduleData Schedule(params int[] sizes)
    {
        return new SweepScheduleData { ChunkSizes = [.. sizes] };
    }

    private static SweepStateData StateOf(IWitJob job)
    {
        var state = job.Variables["state"].Value as SweepStateData;
        Assert.That(state, Is.Not.Null);
        return state!;
    }

    private static SweepScheduleData ScheduleOf(IWitJob job)
    {
        var schedule = job.Variables["schedule"].Value as SweepScheduleData;
        Assert.That(schedule, Is.Not.Null);
        return schedule!;
    }

    #endregion

    #region Resume Tests

    [Test]
    public async Task AFreshRunStartsAtTheZeroCursorAndWritesThePlansScheduleTest()
    {
        var job = m_engine.Compile(SCRIPT);

        var status = await m_engine.ScheduleAndWaitAsync(job, Plan());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        Assert.That(StateOf(job).ChunkIndex, Is.Zero);
        Assert.That(ScheduleOf(job).ChunkSizes, Is.EqualTo(new[] { 3, 3, 1 }), "the schedule variable carries the plan's chunk sizes");
        Assert.That(job.Variables["chunks"].Value, Is.EqualTo(3));
    }

    [Test]
    public async Task AHandedStateAndScheduleAreKeptAndOnlyTheChunksLeftAreCountedTest()
    {
        var job = m_engine.Compile(SCRIPT);
        var manifest = Guid.NewGuid();
        // The fleet was wider at the first run: the handed schedule, not the plan's, is the
        // one the run goes by.
        job.Variables["state"].SetValue(new SweepStateData
        {
            ChunkIndex = 1, NextVariantOrdinal = 6, SucceededCount = 5, FailedCount = 1, ManifestBlobId = manifest
        });
        job.Variables["schedule"].SetValue(Schedule(6, 1));

        var status = await m_engine.ScheduleAndWaitAsync(job, Plan());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        var state = StateOf(job);
        Assert.That(state.ChunkIndex, Is.EqualTo(1), "the cursor is kept");
        Assert.That(state.ManifestBlobId, Is.EqualTo(manifest));
        Assert.That(ScheduleOf(job).ChunkSizes, Is.EqualTo(new[] { 6, 1 }), "the handed schedule is kept");
        Assert.That(job.Variables["chunks"].Value, Is.EqualTo(1), "one chunk left");
    }

    [Test]
    public async Task AStateHandedWithoutAScheduleResumesByThePlansTest()
    {
        var job = m_engine.Compile(SCRIPT);
        job.Variables["state"].SetValue(new SweepStateData
        {
            ChunkIndex = 2, NextVariantOrdinal = 6, SucceededCount = 6, ManifestBlobId = Guid.NewGuid()
        });

        var status = await m_engine.ScheduleAndWaitAsync(job, Plan());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        Assert.That(ScheduleOf(job).ChunkSizes, Is.EqualTo(new[] { 3, 3, 1 }), "the plan's schedule becomes the run's");
        Assert.That(job.Variables["chunks"].Value, Is.EqualTo(1));
    }

    [Test]
    public async Task AScheduleHandedWithoutAStartedStateIsReplacedByThePlansTest()
    {
        var job = m_engine.Compile(SCRIPT);
        job.Variables["schedule"].SetValue(Schedule(7));

        var status = await m_engine.ScheduleAndWaitAsync(job, Plan());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        Assert.That(ScheduleOf(job).ChunkSizes, Is.EqualTo(new[] { 3, 3, 1 }), "a schedule without a cursor means nothing");
        Assert.That(job.Variables["chunks"].Value, Is.EqualTo(3));
    }

    [Test]
    public async Task AFullyHarvestedStateLeavesNothingToRunTest()
    {
        var job = m_engine.Compile(SCRIPT);
        job.Variables["state"].SetValue(new SweepStateData
        {
            ChunkIndex = 3, NextVariantOrdinal = 7, SucceededCount = 7, ManifestBlobId = Guid.NewGuid()
        });
        job.Variables["schedule"].SetValue(Schedule(3, 3, 1));

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
            ChunkIndex = 1, NextVariantOrdinal = 4, SucceededCount = 4, ManifestBlobId = Guid.NewGuid()
        });
        job.Variables["schedule"].SetValue(Schedule(4, 4));

        var status = await m_engine.ScheduleAndWaitAsync(job, Plan());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(status.Message, Does.Contain("cannot resume").And.Contain("covers 8 variant(s), the study has 7"));
    }

    [Test]
    public async Task AScriptWithoutTheScheduleVariableStillRunsTest()
    {
        var job = m_engine.Compile(LEGACY_SCRIPT);

        var status = await m_engine.ScheduleAndWaitAsync(job, Plan());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
        Assert.That(StateOf(job).ChunkIndex, Is.Zero);
        Assert.That(job.Variables["chunks"].Value, Is.EqualTo(3));
    }

    #endregion
}
