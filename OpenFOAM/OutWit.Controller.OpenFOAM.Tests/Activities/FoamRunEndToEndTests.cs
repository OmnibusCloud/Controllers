using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Mock;
using OutWit.Controller.OpenFOAM.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.OpenFOAM.Tests.Activities;

/// <summary>
/// The engine gate: Foam.Run dispatched by Grid.ForEach through the SDK
/// (script compile, task serialisation, mock nodes, blob transport, the fake
/// kit) must carry a wave of variants end to end - a good one, a diverging
/// one recorded as data, a refused one recorded with its reasons - without
/// failing the job.
/// </summary>
[TestFixture]
[NonParallelizable]
public class FoamRunEndToEndTests
{
    #region Constants

    private const string SCRIPT = """
                                  Job:FoamRunTest(FoamTaskCollection:tasks)
                                  {
                                      FoamResultCollection:wave = Grid.ForEach(task in tasks) => Foam.Run(task);
                                  }
                                  """;

    #endregion

    #region Fields

    private string m_blobStoragePath = null!;
    private FoamTestBlobService m_blobService = null!;
    private FakeKit m_kit = null!;
    private IWitEngine m_engine = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var solutionRoot = OpenFOAMTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var controllersPath = OpenFOAMTestPaths.FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        var fakeFoam = OpenFOAMTestPaths.FindFakeFoamPath(solutionRoot);
        if (fakeFoam == null)
            Assert.Ignore("fake-foam not built");

        if (Path.GetTempPath().Contains(' ') && !OperatingSystem.IsWindows())
            Assert.Ignore("the temp path contains a space; OpenFOAM's rule (D-16) refuses it by design");

        m_kit = FakeKit.Create(fakeFoam, "blockMesh", "simpleFoam");
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, m_kit.Root);

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_foam_blobtest_{Guid.NewGuid():N}");
        m_blobService = new FoamTestBlobService(m_blobStoragePath);

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
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, null);
        m_kit?.Dispose();

        if (m_blobStoragePath != null && Directory.Exists(m_blobStoragePath))
            Directory.Delete(m_blobStoragePath, recursive: true);
    }

    #endregion

    #region Tools

    private FoamTaskData Variant(int index, string fakeControl, params FoamFileRefData[] extraFiles)
    {
        var files = new List<FoamFileRefData>
        {
            File("system/controlDict", "FoamFile { object controlDict; }\napplication simpleFoam;\nendTime 3;\n"),
            File("system/fvSchemes", "ddtSchemes { default steadyState; }\n"),
            File("system/fvSolution", "solvers { }\n"),
            // The fake solver's control file is the templated file: the token
            // decides per variant whether the "solve" succeeds or diverges.
            File("system/fake", "{{oc1}}\nITERATIONS=3\n", templated: true),
            File("0/U", "internalField uniform (10 0 0);\n")
        };
        files.AddRange(extraFiles);

        return new FoamTaskData
        {
            VariantIndex = index,
            BaseFiles = files,
            Substitutions = [new FoamTokenValueData { Token = "{{oc1}}", Value = fakeControl }],
            Recipe = new FoamRecipeData
            {
                Application = "simpleFoam",
                Steps = [new FoamStepData { Utility = "blockMesh" }, new FoamStepData { Utility = "simpleFoam" }]
            },
            Threads = 1,
            ArtifactPolicy = new FoamArtifactPolicyData { Times = FoamArtifactTimes.Latest, Logs = true },
            CellCount = 12225,
            SolverClass = "incompressible-steady"
        };
    }

    private FoamFileRefData File(string relativePath, string text, bool templated = false)
    {
        return new FoamFileRefData
        {
            RelativePath = relativePath,
            BlobId = m_blobService.AddText(text),
            Sha256 = "n/a",
            Size = text.Length,
            Templated = templated
        };
    }

    #endregion

    #region Engine Tests

    [Test]
    public void TheScriptCompilesTest()
    {
        var job = m_engine.Compile(SCRIPT);

        Assert.That(job, Is.Not.Null);
        Assert.That(job.Activities.Count, Is.GreaterThan(0));
        Assert.That(job.Variables.Select(variable => variable.Name), Is.SupersetOf(new[] { "tasks", "wave" }));
    }

    [Test]
    public async Task AWaveOfVariantsRunsEndToEndTest()
    {
        var tasks = new List<FoamTaskData?>
        {
            Variant(0, "FAKE-COEFFS"),
            Variant(1, "FAKE-FAIL"),
            Variant(2, "plain", File("0/p", "internalField #codeStream { code #{ os << 0; #}; };\n"))
        };

        var job = m_engine.Compile(SCRIPT);
        var status = await m_engine.ScheduleAndWaitAsync(job, tasks);

        // One diverged variant and one refused case must NOT fail the job -
        // both are rows of data.
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var wave = job.Variables["wave"].Value as IReadOnlyList<FoamResultData?>;
        Assert.That(wave, Is.Not.Null);
        Assert.That(wave!.Count, Is.EqualTo(3));

        // Completion order, never source order: map by VariantIndex.
        var byIndex = wave.Where(result => result != null).ToDictionary(result => result!.VariantIndex, result => result!);
        Assert.That(byIndex.Keys, Is.EquivalentTo(new[] { 0, 1, 2 }));

        var good = byIndex[0];
        Assert.That(good.Rejections, Is.Empty);
        Assert.That(good.ExitCode, Is.EqualTo(0));
        Assert.That(good.Converged, Is.True);
        Assert.That(good.Iterations, Is.EqualTo(3));
        Assert.That(good.Steps.Select(step => step.Utility), Is.EqualTo(new[] { "blockMesh", "simpleFoam" }));
        Assert.That(good.ArtifactBlobId, Is.Not.Null);
        Assert.That(System.IO.File.Exists(m_blobService.GetStoredPath(good.ArtifactBlobId!.Value)), Is.True, "the artifact travelled through the blob service");

        var failed = byIndex[1];
        Assert.That(failed.Rejections, Is.Empty);
        Assert.That(failed.ExitCode, Is.EqualTo(1));
        Assert.That(failed.FailedStep, Is.EqualTo("blockMesh"), "every fake utility reads the same control file");
        Assert.That(failed.LogTail, Does.Contain("FOAM FATAL ERROR"));
        Assert.That(failed.ArtifactBlobId, Is.Null);

        var refused = byIndex[2];
        Assert.That(refused.Rejections, Has.Exactly(1).Items);
        Assert.That(refused.Rejections[0], Does.Contain("codeStream"));
        Assert.That(refused.Steps, Is.Empty);
    }

    #endregion
}
