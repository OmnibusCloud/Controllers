using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Mock;
using OutWit.Controller.OpenFOAM.Tests.Utils;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.OpenFOAM.Tests.Adapters;

/// <summary>
/// What the Foam.Run adapter makes of the kit it finds, through the engine: a
/// kit that is there but cannot be used fails the benchmark with the reason -
/// which takes the node out of the Foam.Run pool - and fails a run with the
/// same reason; no kit folder at all is the default score, not a failure.
/// </summary>
[TestFixture]
[NonParallelizable]
public class WitActivityAdapterFoamRunTests
{
    #region Constants

    private const string ACTIVITY = "Foam.Run";

    private const string SCRIPT = """
                                  Job:FoamRunKitTest(FoamTaskCollection:tasks)
                                  {
                                      FoamResultCollection:wave = Grid.ForEach(task in tasks) => Foam.Run(task);
                                  }
                                  """;

    #endregion

    #region Fields

    private string m_root = null!;
    private string m_controllersPath = null!;
    private FoamTestBlobService m_blobService = null!;
    private IWitEngine m_engine = null!;
    private string? m_previousKitPath;
    private FakeKit? m_kit;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var controllersPath = OpenFOAMTestPaths.FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        m_controllersPath = controllersPath;
        m_previousKitPath = Environment.GetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH);
        m_root = OpenFOAMTestPaths.CreateScratch("foam-adapter");
        m_blobService = new FoamTestBlobService(Path.Combine(m_root, "blobs"));

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
                services.AddSingleton<IWitNodesManager>(new FoamTestNodesManager(WitEngineNodeSdk.Instance));
            });
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_root);
    }

    [TearDown]
    public void RestoreKit()
    {
        // Restored, not cleared: a developer's own override (or the oracle's) outlives this fixture.
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, m_previousKitPath);
        m_kit?.Dispose();
        m_kit = null;
    }

    #endregion

    #region Tools

    private string UnusableKit()
    {
        var root = Path.Combine(m_root, $"broken-kit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, FoamKitEnvironment.FILE_NAME), "# no WM_PROJECT_DIR, no PATH\nKIT_PLATFORM=fake\n");
        return root;
    }

    private static WitBenchmarkOptions QuickBenchmark()
    {
        return new WitBenchmarkOptions { MinDuration = TimeSpan.FromMilliseconds(1), WarmupIterations = 1 };
    }

    private FoamTaskData Variant()
    {
        var text = "FoamFile { object controlDict; }\napplication simpleFoam;\nendTime 1;\n";
        return new FoamTaskData
        {
            VariantIndex = 0,
            Case = new FoamCaseData
            {
                BaseFiles = [new FoamFileRefData { RelativePath = "system/controlDict", BlobId = m_blobService.AddText(text), Sha256 = "n/a", Size = text.Length }],
                Recipe = new FoamRecipeData { Application = "simpleFoam", Steps = [new FoamStepData { Utility = "simpleFoam" }] },
                Threads = 1
            }
        };
    }

    #endregion

    #region Benchmark Tests

    [Test]
    public void TheBenchmarkFailsWithTheReasonOfAKitThatCannotBeUsedTest()
    {
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, UnusableKit());

        var failure = Assert.ThrowsAsync<InvalidOperationException>(() => WitEngineNodeSdk.Instance.RunBenchmark(ACTIVITY, QuickBenchmark()));

        Assert.That(failure!.Message, Does.Contain("unusable KIT.env"), "the reason the node leaves the pool, in words");
    }

    [Test]
    public async Task TheBenchmarkReportsTheDefaultScoreWithoutAKitFolderTest()
    {
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, null);
        var runtimeFolder = FoamKitResolver.ResolveCurrentRuntimeFolder();
        if (runtimeFolder != null && Directory.Exists(Path.Combine(m_controllersPath, "openfoam.module", "openfoam", runtimeFolder)))
            Assert.Ignore("this build's module carries a kit for the platform");

        var result = await WitEngineNodeSdk.Instance.RunBenchmark(ACTIVITY, QuickBenchmark());

        Assert.That(result.Rate, Is.EqualTo(WitBenchmarkResult.Default.Rate), "no kit folder at all is the default score, not a failure");
        Assert.That(result.Iterations, Is.EqualTo(WitBenchmarkResult.Default.Iterations));
    }

    [Test]
    public void TheBenchmarkFailsWithTheReasonOfAKitTooDeepForWindowsTest()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("the depth limit is a Windows one");

        var solutionRoot = OpenFOAMTestPaths.FindSolutionRoot();
        var fakeFoam = solutionRoot == null ? null : OpenFOAMTestPaths.FindFakeFoamPath(solutionRoot);
        if (fakeFoam == null)
            Assert.Ignore("fake-foam not built");

        m_kit = FakeKit.Create(fakeFoam, "blockMesh", "simpleFoam");
        var deep = Path.Combine(new[] { m_root }.Concat(Enumerable.Repeat("deepdeep", 20)).Append("kit").ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(deep) ?? m_root);
        Directory.Move(m_kit.Root, deep);
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, deep);

        var failure = Assert.ThrowsAsync<InvalidOperationException>(() => WitEngineNodeSdk.Instance.RunBenchmark(ACTIVITY, QuickBenchmark()));

        Assert.That(failure!.Message, Does.Contain("deepest file").And.Contain(FoamKitPathRules.MAX_WINDOWS_PATH.ToString()));
    }

    #endregion

    #region Run Tests

    [Test]
    public async Task ARunFailsWithTheReasonOfAKitThatCannotBeUsedTest()
    {
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, UnusableKit());

        var job = m_engine.Compile(SCRIPT);
        var status = await m_engine.ScheduleAndWaitAsync(job, new List<FoamTaskData?> { Variant() });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed), "a node that cannot run OpenFOAM is an infrastructure failure, not a red variant");
        Assert.That(status.Message, Does.Contain("unusable KIT.env"), "the reason travels with the failure");
    }

    #endregion
}
