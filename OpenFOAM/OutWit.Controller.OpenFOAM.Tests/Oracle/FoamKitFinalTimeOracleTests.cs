using System.IO.Compression;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Mock;
using OutWit.Controller.OpenFOAM.Tests.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Tests.Oracle;

/// <summary>
/// Every response kind against the REAL kit on a case whose final time is
/// not a written time: the lid-driven cavity to 0.045 s, written every
/// 0.02 s. The recipe is the one an initiator composes - a force added to
/// the solve, every other kind measured after it by a post step on the
/// latest time - and before this the post steps read 0.04, the last written
/// time, so the statistics, the min/max and the probes came back blank as
/// stale (measured on v2606, 2026-10-28). The controller now measures those
/// kinds during the solve as well, and the extractor reports the row at the
/// final time whichever step wrote it; a probe outside the mesh is an empty
/// cell with a note; the case's own function object, which writes at write
/// times only, stays the case's and is reported stale by name.
/// </summary>
[TestFixture]
[Category("Kit")]
public class FoamKitFinalTimeOracleTests
{
    #region Constants

    private const string KIT_VARIABLE = FoamKitResolver.ENV_KIT_PATH;

    private const string FIXTURE = "CavityWrite";

    private const double FINAL_TIME = 0.045;

    private const double LAST_WRITTEN_TIME = 0.04;

    #endregion

    #region Fields

    private string m_storage = null!;

    private FoamTestBlobService m_blobs = null!;

    private FoamKit m_kit = null!;

    #endregion

    [SetUp]
    public void Setup()
    {
        var kitPath = Environment.GetEnvironmentVariable(KIT_VARIABLE);
        if (string.IsNullOrEmpty(kitPath))
            Assert.Ignore($"{KIT_VARIABLE} is not set; the kit oracle runs only where a kit is unpacked");

        m_kit = FoamKitResolver.Resolve(typeof(FoamKit).Assembly.Location)
                ?? throw new InvalidOperationException($"{KIT_VARIABLE}={kitPath} does not resolve to a kit");
        m_storage = OpenFOAMTestPaths.CreateScratch("foam-oracle-final-time");
        m_blobs = new FoamTestBlobService(m_storage);
    }

    [TearDown]
    public void TearDown()
    {
        if (m_storage != null)
            OpenFOAMTestPaths.TryDelete(m_storage);
    }

    #region Oracle Tests

    [Test]
    public async Task EveryKindIsReportedAtAFinalTimeThatIsNotWrittenTest()
    {
        var session = new FoamCaseSession(m_kit, m_blobs, new WitTempStorageDefault(m_storage));

        var result = await session.RunAsync(Task());

        TestContext.Out.WriteLine(result);
        TestContext.Out.WriteLine("steps:     " + string.Join(", ", result.Steps));
        TestContext.Out.WriteLine("responses: " + string.Join(", ", result.ResponseRow?.Values.Select(entry => $"{entry.Name}={entry.Value}") ?? []));
        if (result.LogTail != null)
            TestContext.Out.WriteLine(result.LogTail);

        Assert.That(result.Rejections, Is.Empty);
        Assert.That(result.ExitCode, Is.EqualTo(0), result.LogTail);
        Assert.That(result.FinalTime, Is.EqualTo(FINAL_TIME).Within(1e-9));
        Assert.That(result.Steps.Select(step => (step.Utility, step.Ranks)), Is.EqualTo(new[]
        {
            ("blockMesh", 1),
            ("includeFunc", 0), ("includeFunc", 0), ("includeFunc", 0), ("includeFunc", 0), ("includeFunc", 0),
            ("icoFoam", 1),
            ("postProcess", 0), ("postProcess", 0), ("postProcess", 0), ("postProcess", 0)
        }), "the recipe's include, then the controller's four, right before the solve; the post steps for what the solver measured are not run");

        var names = result.ResponseRow!.Values.Select(value => value.Name).ToList();
        Assert.That(names, Does.Contain("patchValue1.areaAverage(p)"));
        Assert.That(names, Does.Contain("volumeValue1.volAverage(p)"));
        Assert.That(names, Does.Contain("fieldMinMax1.min(p)").And.Contain("fieldMinMax1.max(U)"));
        Assert.That(names, Does.Contain("probe1.p@probe0(0.05 0.05 0.005)").And.Contain("probe1.U@probe0(0.05 0.05 0.005)_x"));
        Assert.That(names, Does.Contain("forces1.force.total_x").And.Contain("forces1.moment.total_z"));
        Assert.That(names.Where(name => name.Contains("probe1(", StringComparison.Ordinal)), Is.Empty, "the probe outside the mesh is an empty cell");
        Assert.That(names.Where(name => name.StartsWith("ownMinMax", StringComparison.Ordinal)), Is.Empty, "the case's own function object wrote at 0.04 last: stale, not reported");

        var notes = ArtifactText(result, "log.responses");
        Assert.That(notes, Does.Contain("probe1: probe 1 (9 9 9) of p is outside the mesh - not reported."));
        Assert.That(notes, Does.Contain("probe1: probe 1 (9 9 9) of U is outside the mesh - not reported."));
        Assert.That(notes, Does.Contain($"ownMinMax: the last row of fieldMinMax.dat is at time {LAST_WRITTEN_TIME}, the run ended at {FINAL_TIME} - not reported."));
        Assert.That(notes, Does.Not.Contain("patchValue1:").And.Not.Contain("volumeValue1:").And.Not.Contain("fieldMinMax1:"));

        // The solve's own files carry the final time, and the post steps
        // that would have read 0.04 - and, for the probes, overwritten the
        // solve's rows with that one - did not run.
        Assert.That(ArtifactText(result, "postProcessing/patchValue1/0/surfaceFieldValue.dat").TrimEnd().Split('\n')[^1], Does.StartWith("0.045"), "the solve's file: the final time");
        Assert.That(ArtifactText(result, "postProcessing/probe1/0/p").TrimEnd().Split('\n')[^1], Does.StartWith("0.045"), "the probe's rows are the solve's, to the final time");
        Assert.That(ArtifactEntries(result).Where(entry => entry.StartsWith("postProcessing/patchValue1/", StringComparison.Ordinal)),
            Is.EqualTo(new[] { "postProcessing/patchValue1/0/surfaceFieldValue.dat" }), $"no {LAST_WRITTEN_TIME}/ directory and no _0 sibling of a post step");
        Assert.That(ArtifactText(result, "log.includeFunc.2"), Does.Contain("'#includeFunc patchValue1'").And.Contain("latest written time"));
        Assert.That(ArtifactText(result, "log.postProcess"), Does.StartWith("Not run: 'postProcess -func patchValue1 -latestTime'"));
        Assert.That(ArtifactText(result, "system/controlDict"), Does.Contain("#includeFunc forces1").And.Contain("#includeFunc probe1"));
    }

    #endregion

    #region Tools

    private FoamTaskData Task()
    {
        var fixture = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", FIXTURE);
        var files = Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories)
            .Select(file =>
            {
                return m_blobs.AddFile(Path.GetRelativePath(fixture, file).Replace('\\', '/'), File.ReadAllBytes(file));
            })
            .ToList();

        return new FoamTaskData
        {
            VariantIndex = 1,
            Case = new FoamCaseData
            {
                BaseFiles = files,
                Recipe = new FoamRecipeData
                {
                    Application = "icoFoam",
                    Steps =
                    [
                        new FoamStepData { Utility = "blockMesh" },
                        new FoamStepData { Utility = "includeFunc", Arguments = ["forces1"] },
                        new FoamStepData { Utility = "icoFoam" },
                        new FoamStepData { Utility = "postProcess", Arguments = ["-func", "patchValue1", "-latestTime"] },
                        new FoamStepData { Utility = "postProcess", Arguments = ["-func", "volumeValue1", "-latestTime"] },
                        new FoamStepData { Utility = "postProcess", Arguments = ["-func", "fieldMinMax1", "-latestTime"] },
                        new FoamStepData { Utility = "postProcess", Arguments = ["-func", "probe1", "-latestTime"] }
                    ]
                },
                Threads = 1,
                Extraction = new FoamExtractionRequestData
                {
                    Responses =
                    [
                        new FoamResponseSpecData { Name = "patchValue1", Kind = FoamResponseKind.PatchValue, Patches = ["movingWall"], Fields = ["p"], Operation = "areaAverage" },
                        new FoamResponseSpecData { Name = "volumeValue1", Kind = FoamResponseKind.VolumeValue, Fields = ["p"], Operation = "volAverage" },
                        new FoamResponseSpecData { Name = "fieldMinMax1", Kind = FoamResponseKind.FieldMinMax, Fields = ["p", "U"] },
                        new FoamResponseSpecData
                        {
                            Name = "probe1", Kind = FoamResponseKind.Probe, Fields = ["p", "U"],
                            Parameters = [new FoamNamedValueData { Name = "probeLocations", Value = "((0.05 0.05 0.005) (9 9 9))" }]
                        },
                        new FoamResponseSpecData
                        {
                            Name = "forces1", Kind = FoamResponseKind.Forces, Patches = ["movingWall"],
                            Parameters =
                            [
                                new FoamNamedValueData { Name = "rho", Value = "rhoInf" },
                                new FoamNamedValueData { Name = "rhoInf", Value = "1" },
                                new FoamNamedValueData { Name = "CofR", Value = "(0 0 0)" }
                            ]
                        },
                        // The case's own, read from what the case writes: no step of its own.
                        new FoamResponseSpecData { Name = "ownMinMax", Kind = FoamResponseKind.FieldMinMax, Fields = ["p"] }
                    ]
                },
                ArtifactPolicy = new FoamArtifactPolicyData { Logs = true, PostProcessing = true },
                CellCount = 100,
                SolverClass = "incompressible-transient"
            }
        };
    }

    /// <summary>The entry names of the result's artifact.</summary>
    private List<string> ArtifactEntries(FoamResultData result)
    {
        Assert.That(result.ArtifactBlobId, Is.Not.Null);
        using var archive = ZipFile.OpenRead(m_blobs.GetStoredPath(result.ArtifactBlobId!.Value));
        return archive.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    /// <summary>A file of the result's artifact, as text with LF line endings.</summary>
    private string ArtifactText(FoamResultData result, string entryName)
    {
        Assert.That(result.ArtifactBlobId, Is.Not.Null);
        using var archive = ZipFile.OpenRead(m_blobs.GetStoredPath(result.ArtifactBlobId!.Value));
        var entry = archive.GetEntry(entryName);
        Assert.That(entry, Is.Not.Null, $"the artifact carries {entryName}");

        using var reader = new StreamReader(entry!.Open());
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    #endregion
}
