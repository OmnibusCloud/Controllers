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
/// The OpenFOAM family through the engine: the bundled SweepOpenFOAM.wit
/// over Foam.Run against the fake kit carries a chunked case study end to end
/// - one case, each variant its token values, substituted on the node - with
/// a succeeded, a failed and a node-refused variant in one study, all rows;
/// the plan refuses what the case's data or the templated files already say
/// is wrong, before any node sees a task; and a script that declares the
/// other family's collections fails loudly instead of fanning out nothing.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SweepOpenFOAMTests
{
    #region Fields

    private string m_blobStoragePath = null!;
    private SweepTestBlobService m_blobService = null!;
    private string m_script = null!;
    private string m_calculiXScript = null!;
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

        var scriptPath = SweepTestPaths.GetScriptPath(solutionRoot, "SweepOpenFOAM");
        if (!File.Exists(scriptPath))
            Assert.Ignore($"SweepOpenFOAM.wit not found at {scriptPath}");

        m_script = File.ReadAllText(scriptPath);
        m_calculiXScript = File.ReadAllText(SweepTestPaths.GetScriptPath(solutionRoot, "SweepCalculiX"));

        var controllersPath = SweepTestPaths.FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        var fakeFoam = OpenFOAMTestPaths.FindFakeFoamPath(solutionRoot);
        if (fakeFoam == null)
            Assert.Ignore("fake-foam not built");

        if (Path.GetTempPath().Contains(' ') && !OperatingSystem.IsWindows())
            Assert.Ignore("the temp path contains a space; OpenFOAM strips whitespace from paths, so the controller refuses it by design");

        m_kit = FakeKit.Create(fakeFoam, "blockMesh", "simpleFoam");
        m_previousKitPath = Environment.GetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH);
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, m_kit.Root);

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_sweep_foam_{Guid.NewGuid():N}");
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

    private FoamFileRefData BlobFile(string relativePath, string text, bool templated = false)
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

    /// <summary>
    /// A case whose fake solver is driven by the swept value: system/fake is
    /// the templated file, so the token decides per variant whether the
    /// "solve" converges, diverges or carries run-time code.
    /// </summary>
    private FoamCaseData FakeCase()
    {
        return new FoamCaseData
        {
            BaseFiles =
            [
                BlobFile("system/controlDict", "FoamFile { object controlDict; }\napplication simpleFoam;\nendTime 3;\n"),
                BlobFile("system/fvSchemes", "ddtSchemes { default steadyState; }\n"),
                BlobFile("system/fvSolution", "solvers { }\n"),
                BlobFile("system/fake", "{{oc1}}\nITERATIONS=3\n", templated: true),
                BlobFile("0/U", "internalField uniform (10 0 0);\n")
            ],
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

    private static SweepOptionsData Study(FoamCaseData data, params string[] values)
    {
        return new SweepOptionsData
        {
            Parameters = [new SweepParameterData { Name = "control", Token = "{{oc1}}" }],
            Variants = values.Select((value, index) => new SweepVariantData { VariantIndex = index, Values = [value] }).ToList(),
            FirstChunkSize = 2,
            MaxChunkSize = 2,
            OpenFOAM = data
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
        Assert.That(variables, Is.SupersetOf(new[] { "opts", "plan", "state", "chunks", "tasks", "wave", "manifest" }));
    }

    #endregion

    #region Study Tests

    [Test]
    public async Task ACaseStudyRunsEndToEndWithEveryOutcomeAsARowTest()
    {
        // Four variants in two chunks (the fleet of three raises the first to
        // three): converging, diverging, carrying run-time code in its value,
        // converging again.
        var values = new[] { "FAKE-COEFFS", "FAKE-FAIL", "#codeStream { code #{ #}; }", "plain" };
        var job = m_engine.Compile(m_script);

        var status = await m_engine.ScheduleAndWaitAsync(job, Study(FakeCase(), values));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), "a failed and a refused variant are rows, never a failed job");

        var state = job.Variables["state"].Value as SweepStateData ?? new SweepStateData();
        Assert.That(state.ChunkIndex, Is.EqualTo(2));
        Assert.That(state.SucceededCount, Is.EqualTo(2));
        Assert.That(state.FailedCount, Is.EqualTo(1));
        Assert.That(state.RefusedCount, Is.EqualTo(1));

        var manifest = MemoryPackSerializer.Deserialize<SweepManifestData>(
            await File.ReadAllBytesAsync(m_blobService.GetStoredPath(state.ManifestBlobId ?? Guid.Empty))) ?? new SweepManifestData();
        Assert.That(manifest.Rows.Select(row => row.VariantIndex), Is.EqualTo(new[] { 0, 1, 2, 3 }));
        Assert.That(manifest.Rows.All(row => row.OpenFOAM != null && row.CalculiX == null), Is.True, "every row carries the OpenFOAM result, verbatim");
        Assert.That(manifest.Rows.Select(row => row.Outcome), Is.EqualTo(new[] { SweepOutcome.Succeeded, SweepOutcome.Failed, SweepOutcome.Refused, SweepOutcome.Succeeded }));

        var good = manifest.Rows[0].OpenFOAM ?? new FoamResultData();
        Assert.That(good.Converged, Is.True);
        Assert.That(good.Iterations, Is.EqualTo(3));
        Assert.That(good.ArtifactBlobId, Is.Not.Null);

        var failed = manifest.Rows[1].OpenFOAM ?? new FoamResultData();
        Assert.That(failed.FailedStep, Is.EqualTo("blockMesh"), "every fake utility reads the same control file");
        Assert.That(failed.ArtifactBlobId, Is.Not.Null, "the policy asked for logs: a failed run delivers them");

        var refused = manifest.Rows[2].OpenFOAM ?? new FoamResultData();
        Assert.That(refused.Rejections, Has.Some.Contains("codeStream"));
        Assert.That(refused.Steps, Is.Empty, "nothing ran");

        // The index names each artifact's kind; a refused variant has none.
        Assert.That(state.Results.Select(entry => entry.Label), Is.EqualTo(values.Select(value => $"control={value}")));
        Assert.That(state.Results[0].Artifacts.Single().Kind, Is.EqualTo(SweepArtifactKind.OpenFOAMCase));
        Assert.That(state.Results[0].Artifacts.Single().Bytes, Is.EqualTo(good.ArtifactBytes));
        Assert.That(state.Results[2].Artifacts, Is.Empty);
    }

    #endregion

    #region Refusal Tests

    [Test]
    public async Task ARecipeOutsideTheAllowListIsRefusedBeforeAnyNodeRunsTest()
    {
        var data = FakeCase();
        data.Recipe = new FoamRecipeData { Application = "simpleFoam", Steps = [new FoamStepData { Utility = "bash" }, new FoamStepData { Utility = "simpleFoam" }] };
        var job = m_engine.Compile(m_script);

        var status = await m_engine.ScheduleAndWaitAsync(job, Study(data, "plain"));

        Assert.That(status.Result, Is.Not.EqualTo(WitProcessingResult.Completed));
        Assert.That(status.Message, Does.Contain("Step 1: 'bash' is not on the allow-list."), "the OpenFOAM model's own sentence");
        Assert.That(job.Variables["state"].Value, Is.Null, "the plan refused the study: no chunk ever ran");
    }

    [Test]
    public async Task ATokenNoTemplatedFileCarriesIsRefusedBeforeAnyNodeRunsTest()
    {
        var options = Study(FakeCase(), "plain");
        options.Parameters[0].Token = "{{oc7}}";
        var job = m_engine.Compile(m_script);

        var status = await m_engine.ScheduleAndWaitAsync(job, options);

        Assert.That(status.Result, Is.Not.EqualTo(WitProcessingResult.Completed));
        Assert.That(status.Message, Does.Contain("Token {{oc7}} occurs in no templated file of the case."));
        Assert.That(status.Message, Does.Contain("system/fake: token {{oc1}} is not declared by the study."));
        Assert.That(job.Variables["state"].Value, Is.Null);
    }

    [Test]
    public async Task ACaseStudyUnderTheCalculiXScriptFailsLoudlyTest()
    {
        // The CalculiX script declares CcxTaskCollection: the engine would
        // leave it empty for OpenFOAM tasks and the grid fan out nothing - the
        // host reads the collection back and stops the job at the first chunk.
        var job = m_engine.Compile(m_calculiXScript);

        var status = await m_engine.ScheduleAndWaitAsync(job, Study(FakeCase(), "plain"));

        Assert.That(status.Result, Is.Not.EqualTo(WitProcessingResult.Completed));
        Assert.That(status.Message, Does.Contain("does not hold FoamTaskData: OpenFOAM studies run with the SweepOpenFOAM script"));
    }

    #endregion
}
