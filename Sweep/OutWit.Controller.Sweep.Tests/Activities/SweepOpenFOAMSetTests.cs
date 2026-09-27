using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;
using OutWit.Controller.Sweep.Model;
using OutWit.Controller.Sweep.Tests.Mock;
using OutWit.Controller.Sweep.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Sweep.Tests.Activities;

/// <summary>
/// The OpenFOAM case set through the engine: the bundled SweepOpenFOAMSet.wit
/// over Foam.Run against the fake kit runs every variant on its own case -
/// its own tree, its own recipe - with the study's shared threads, responses
/// and artifacts, every outcome a row labelled by its case; the plan refuses
/// a set whose case breaks the OpenFOAM rules, naming the case, and a set
/// beside a CalculiX study, before any node sees a task.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SweepOpenFOAMSetTests
{
    #region Fields

    private string m_blobStoragePath = null!;
    private SweepTestBlobService m_blobService = null!;
    private string m_script = null!;
    private FakeKit m_kit = null!;
    private IWitEngine m_engine = null!;
    private string? m_previousKitPath;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var solutionRoot = SweepTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var scriptPath = SweepTestPaths.GetScriptPath(solutionRoot, "SweepOpenFOAMSet");
        if (!File.Exists(scriptPath))
            Assert.Ignore($"SweepOpenFOAMSet.wit not found at {scriptPath}");

        m_script = File.ReadAllText(scriptPath);

        var controllersPath = SweepTestPaths.FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        var fakeFoam = OpenFOAMTestPaths.FindFakeFoamPath(solutionRoot);
        if (fakeFoam == null)
            Assert.Ignore("fake-foam not built");

        if (Path.GetTempPath().Contains(' ') && !OperatingSystem.IsWindows())
            Assert.Ignore("the temp path contains a space; OpenFOAM strips whitespace from paths, so the controller refuses it by design");

        m_kit = FakeKit.Create(fakeFoam, "blockMesh", "icoFoam");
        m_previousKitPath = Environment.GetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH);
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, m_kit.Root);

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_sweep_foam_set_{Guid.NewGuid():N}");
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
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, m_previousKitPath);
        m_kit?.Dispose();

        if (m_blobStoragePath != null && Directory.Exists(m_blobStoragePath))
            Directory.Delete(m_blobStoragePath, recursive: true);
    }

    #endregion

    #region Tools

    private FoamFileRefData BlobFile(string relativePath, string text)
    {
        return new FoamFileRefData
        {
            RelativePath = relativePath,
            BlobId = m_blobService.AddText(text),
            Sha256 = "n/a",
            Size = text.Length
        };
    }

    /// <summary>
    /// A ready case whose fake solver is driven by its own system/fake - the
    /// set's cases differ in content, not in substituted values - and whose
    /// extra file may carry what the node refuses.
    /// </summary>
    private SweepOpenFOAMCaseData ReadyCase(int variantIndex, string name, string fake, string pressure = "internalField uniform 0;\n")
    {
        return new SweepOpenFOAMCaseData
        {
            VariantIndex = variantIndex,
            Name = name,
            BaseFiles =
            [
                BlobFile("system/controlDict", "FoamFile { object controlDict; }\napplication icoFoam;\nendTime 3;\n"),
                BlobFile("system/fvSchemes", "ddtSchemes { default Euler; }\n"),
                BlobFile("system/fvSolution", "solvers { }\n"),
                BlobFile("system/fake", fake),
                BlobFile("0/U", "internalField uniform (0 0 0);\n"),
                BlobFile("0/p", pressure)
            ],
            Recipe = new FoamRecipeData
            {
                Application = "icoFoam",
                Steps = [new FoamStepData { Utility = "blockMesh" }, new FoamStepData { Utility = "icoFoam" }]
            },
            CellCount = 400L * (variantIndex + 1),
            SolverClass = "incompressible-transient"
        };
    }

    private static SweepOptionsData Study(int variants)
    {
        return new SweepOptionsData
        {
            Variants = Enumerable.Range(0, variants).Select(index => new SweepVariantData { VariantIndex = index }).ToList(),
            FirstChunkSize = 2,
            MaxChunkSize = 2,
            OpenFOAM = new FoamCaseData
            {
                Threads = 1,
                ArtifactPolicy = new FoamArtifactPolicyData { Times = FoamArtifactTimes.Latest, Logs = true }
            }
        };
    }

    #endregion

    #region Script Tests

    [Test]
    public void TheBundledScriptCompilesTest()
    {
        var job = m_engine.Compile(m_script);

        Assert.That(job, Is.Not.Null);
        var variables = job.Variables.Select(variable => variable.Name).ToList();
        Assert.That(variables, Is.SupersetOf(new[] { "opts", "cases", "plan", "state", "chunks", "tasks", "wave", "manifest" }));
    }

    #endregion

    #region Study Tests

    [Test]
    public async Task EveryVariantRunsItsOwnCaseAndIsLabelledByItTest()
    {
        var set = new SweepOpenFOAMSetData
        {
            Cases =
            [
                ReadyCase(0, "cavity-coarse", "ITERATIONS=2\n"),
                ReadyCase(1, "cavity-broken", "FAKE-FAIL\n"),
                ReadyCase(2, "cavity-coded", "ITERATIONS=3\n", "internalField #codeStream { code #{ #}; };\n"),
                ReadyCase(3, "cavity-fine", "ITERATIONS=4\n")
            ]
        };
        var job = m_engine.Compile(m_script);

        var status = await m_engine.ScheduleAndWaitAsync(job, Study(4), set);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), "a failed and a refused case are rows, never a failed job");

        var state = job.Variables["state"].Value as SweepStateData ?? new SweepStateData();
        Assert.That((state.SucceededCount, state.FailedCount, state.RefusedCount), Is.EqualTo((2, 1, 1)));

        var manifest = MemoryPackSerializer.Deserialize<SweepManifestData>(
            await File.ReadAllBytesAsync(m_blobService.GetStoredPath(state.ManifestBlobId ?? Guid.Empty))) ?? new SweepManifestData();
        Assert.That(manifest.Rows.Select(row => row.Outcome), Is.EqualTo(new[] { SweepOutcome.Succeeded, SweepOutcome.Failed, SweepOutcome.Refused, SweepOutcome.Succeeded }));
        Assert.That(manifest.Rows[0].OpenFOAM?.Iterations, Is.EqualTo(2), "the coarse case's own control file");
        Assert.That(manifest.Rows[3].OpenFOAM?.Iterations, Is.EqualTo(4), "the fine case's own control file");
        Assert.That(manifest.Rows[2].OpenFOAM?.Rejections, Has.Some.Contains("codeStream"), "the node reads each case's own content");
        Assert.That(manifest.Rows[0].OpenFOAM?.ArtifactBlobId, Is.Not.Null, "the study's shared artifact policy");

        Assert.That(state.Results.Select(entry => entry.Label), Is.EqualTo(new[] { "cavity-coarse", "cavity-broken", "cavity-coded", "cavity-fine" }));
    }

    #endregion

    #region Refusal Tests

    [Test]
    public async Task ACaseOutsideTheRulesIsRefusedByNameBeforeAnyNodeRunsTest()
    {
        var broken = ReadyCase(1, "cavity-fine", "ITERATIONS=2\n");
        broken.Recipe!.Steps.Insert(0, new FoamStepData { Utility = "bash" });
        var set = new SweepOpenFOAMSetData { Cases = [ReadyCase(0, "cavity-coarse", "ITERATIONS=2\n"), broken] };
        var job = m_engine.Compile(m_script);

        var status = await m_engine.ScheduleAndWaitAsync(job, Study(2), set);

        Assert.That(status.Result, Is.Not.EqualTo(WitProcessingResult.Completed));
        Assert.That(status.Message, Does.Contain("Case 'cavity-fine': Step 1: 'bash' is not on the allow-list."));
        Assert.That(job.Variables["state"].Value, Is.Null, "the plan refused the set: no chunk ever ran");
    }

    [Test]
    public async Task ACaseSetBesideACalculiXStudyIsRefusedTest()
    {
        var options = Study(1);
        options.OpenFOAM = null;
        options.CalculiX = new SweepCalculiXStudyData { Decks = [new SweepCalculiXDeckData { VariantIndex = 0, DeckBlobId = m_blobService.AddText("*NODE\n") }] };
        var set = new SweepOpenFOAMSetData { Cases = [ReadyCase(0, "cavity-coarse", "ITERATIONS=2\n")] };
        var job = m_engine.Compile(m_script);

        var status = await m_engine.ScheduleAndWaitAsync(job, options, set);

        Assert.That(status.Result, Is.Not.EqualTo(WitProcessingResult.Completed));
        Assert.That(status.Message, Does.Contain("A case set rides only with an OpenFOAM study."));
        Assert.That(job.Variables["state"].Value, Is.Null);
    }

    #endregion
}
