using System.Diagnostics;
using System.IO.Compression;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Mock;
using OutWit.Controller.OpenFOAM.Tests.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

/// <summary>
/// The whole of Foam.Run below the adapter, against the fake solver: a case
/// that runs, a case that is refused, a case that fails. What the adapter
/// adds is the pool plumbing and the kit lookup, tested elsewhere.
/// </summary>
[TestFixture]
public class FoamCaseSessionTests
{
    private string m_storage = null!;
    private FoamTestBlobService m_blobs = null!;
    private FakeKit? m_kit;

    [SetUp]
    public void Setup()
    {
        m_storage = OpenFOAMTestPaths.CreateScratch("foam-blobs");
        m_blobs = new FoamTestBlobService(m_storage);
    }

    [TearDown]
    public void TearDown()
    {
        m_kit?.Dispose();
        OpenFOAMTestPaths.TryDelete(m_storage);
    }

    #region Tools

    private FoamCaseSession RequireSession()
    {
        var solutionRoot = OpenFOAMTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var fakeFoam = OpenFOAMTestPaths.FindFakeFoamPath(solutionRoot);
        if (fakeFoam == null)
            Assert.Ignore("fake-foam not built");

        // On Windows the scratch takes the 8.3 form of a spaced temp path; elsewhere there is no such form.
        if (Path.GetTempPath().Contains(' ') && !OperatingSystem.IsWindows())
            Assert.Ignore("the temp path contains a space; OpenFOAM strips whitespace from paths, so the controller refuses it by design");

        m_kit = FakeKit.Create(fakeFoam, "blockMesh", "simpleFoam", "checkMesh");
        return new FoamCaseSession(m_kit.Resolve(), m_blobs, new WitTempStorageDefault(m_storage));
    }

    private static FoamTaskData Task(FoamCaseData data, params FoamTokenValueData[] substitutions)
    {
        return new FoamTaskData
        {
            VariantIndex = 3,
            Case = data,
            Substitutions = substitutions.Length > 0
                ? [.. substitutions]
                :
                [
                    new FoamTokenValueData { Token = "{{oc1}}", Value = "1e-05" },
                    new FoamTokenValueData { Token = "{{oc2}}", Value = "10" }
                ]
        };
    }

    private FoamTaskData PitzTask(string fakeControl, bool templated = true)
    {
        return Task(PitzCase(fakeControl, templated));
    }

    private FoamCaseData PitzCase(string fakeControl, bool templated = true)
    {
        return new FoamCaseData
        {
            BaseFiles =
            [
                BlobFile("system/controlDict", "FoamFile { object controlDict; }\napplication simpleFoam;\nendTime 5;\n"),
                BlobFile("system/fvSchemes", "ddtSchemes { default steadyState; }\n"),
                BlobFile("system/fvSolution", "solvers { }\n"),
                BlobFile("system/fake", fakeControl),
                BlobFile("constant/transportProperties", "nu {{oc1}};\n", templated),
                BlobFile("0/U", "internalField uniform ({{oc2}} 0 0);\n", templated)
            ],
            Recipe = new FoamRecipeData
            {
                Application = "simpleFoam",
                Steps =
                [
                    new FoamStepData { Utility = "blockMesh" },
                    new FoamStepData { Utility = "checkMesh" },
                    new FoamStepData { Utility = "simpleFoam", Parallel = true }
                ]
            },
            Threads = 2,
            Extraction = new FoamExtractionRequestData { Responses = [new FoamResponseSpecData { Name = "coeffs", Kind = FoamResponseKind.ForceCoeffs, Patches = ["wall"] }] },
            ArtifactPolicy = new FoamArtifactPolicyData { Times = FoamArtifactTimes.Latest, Logs = true, PostProcessing = true },
            CellCount = 12225,
            SolverClass = "incompressible-steady"
        };
    }

    private FoamFileRefData BlobFile(string relativePath, string text, bool templated = false)
    {
        return new FoamFileRefData
        {
            RelativePath = relativePath,
            BlobId = m_blobs.AddText(text),
            Sha256 = "n/a",
            Size = text.Length,
            Templated = templated
        };
    }

    #endregion

    #region Session Tests

    [Test]
    public async Task ACaseRunsToAResultWithFactsResponsesAndAnArtifactTest()
    {
        var session = RequireSession();

        var result = await session.RunAsync(PitzTask("FAKE-COEFFS\nITERATIONS=4\n"));

        Assert.That(result.Rejections, Is.Empty);
        Assert.That(result.VariantIndex, Is.EqualTo(3));
        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.FailedStep, Is.Null);
        Assert.That(result.Steps.Select(step => step.Utility), Is.EqualTo(new[] { "blockMesh", "checkMesh", "simpleFoam" }));
        Assert.That(result.Steps[2].Ranks, Is.EqualTo(1), "the fake kit has no MPI launcher");
        Assert.That(result.Converged, Is.True);
        Assert.That(result.Iterations, Is.EqualTo(4));
        Assert.That(result.FinalTime, Is.EqualTo(4));
        Assert.That(result.FinalResiduals.Select(residual => residual.Name), Is.EqualTo(new[] { "residual.Ux", "residual.p" }));
        Assert.That(result.WarningCount, Is.EqualTo(3), "one warning per fake step");
        Assert.That(result.CellCount, Is.EqualTo(12225));
        Assert.That(result.CheckMeshVerdict, Is.Null, "the fake checkMesh prints no verdict");
        Assert.That(result.TotalSeconds, Is.GreaterThan(0));

        Assert.That(result.ResponseRow, Is.Not.Null);
        Assert.That(result.ResponseRow!.Values.Select(value => value.Name), Is.EqualTo(new[] { "coeffs.Cd", "coeffs.Cl", "coeffs.CmPitch" }));
        Assert.That(result.ResponseRow.Values[0].Value, Is.EqualTo(0.418));

        Assert.That(result.ArtifactBlobId, Is.Not.Null);
        Assert.That(result.ArtifactBytes, Is.GreaterThan(0));
        using var archive = ZipFile.OpenRead(m_blobs.GetStoredPath(result.ArtifactBlobId!.Value));
        var entries = archive.Entries.Select(entry => entry.FullName).ToList();
        Assert.That(entries, Does.Contain("4/U").And.Contain("log.simpleFoam").And.Contain("postProcessing/coeffs/4/coefficient.dat").And.Contain("case.foam"));
        Assert.That(entries, Does.Contain("constant/transportProperties"));
        Assert.That(entries, Does.Contain("system/controlDict").And.Contain("system/fake").And.Contain("system/coeffs"), "system/ travels whole, the written function object included");
        Assert.That(entries, Does.Not.Contain("log.checkMesh").Or.Contain("log.checkMesh"), "logs travel by policy; this policy asked for them");
        Assert.That(entries, Does.Contain("log.blockMesh").And.Contain("log.checkMesh"));
    }

    [Test]
    public async Task TheSubstitutedFilesReachTheSolverTest()
    {
        var session = RequireSession();
        var data = PitzCase("FAKE-ECHO\nITERATIONS=1\n");
        data.ArtifactPolicy = new FoamArtifactPolicyData { Times = FoamArtifactTimes.Latest };

        var result = await session.RunAsync(Task(data));

        Assert.That(result.Rejections, Is.Empty);
        using var archive = ZipFile.OpenRead(m_blobs.GetStoredPath(result.ArtifactBlobId!.Value));
        using var reader = new StreamReader(archive.GetEntry("constant/transportProperties")!.Open());
        Assert.That(await reader.ReadToEndAsync(), Is.EqualTo("nu 1e-05;\n"));
    }

    [Test]
    public async Task ACaseWithRunTimeCodeIsRefusedBeforeAnythingRunsTest()
    {
        var session = RequireSession();
        var data = PitzCase("ITERATIONS=1\n");
        data.BaseFiles.Add(BlobFile("0/p", "internalField #codeStream { code #{ os << 0; #}; };\n"));

        var result = await session.RunAsync(Task(data));

        Assert.That(result.Rejections, Has.Exactly(1).Items);
        Assert.That(result.Rejections[0], Does.StartWith("0/p:1:").And.Contain("codeStream"));
        Assert.That(result.Steps, Is.Empty);
        Assert.That(result.ArtifactBlobId, Is.Null);
        Assert.That(result.ResponseRow, Is.Null);
    }

    [Test]
    public async Task AVariantMissingATokenValueIsRefusedTest()
    {
        var session = RequireSession();
        var task = Task(PitzCase("ITERATIONS=1\n"), new FoamTokenValueData { Token = "{{oc1}}", Value = "1e-05" });

        var result = await session.RunAsync(task);

        Assert.That(result.Rejections, Is.EqualTo(new[] { "0/U: token {{oc2}} has no value in this variant." }));
        Assert.That(result.Steps, Is.Empty);
    }

    [Test]
    public async Task ARecipeOutsideTheAllowListIsRefusedTogetherWithFileFindingsTest()
    {
        var session = RequireSession();
        var data = PitzCase("ITERATIONS=1\n");
        data.Recipe = new FoamRecipeData
        {
            Application = "simpleFoam",
            Steps = [new FoamStepData { Utility = "foamyHexMesh" }, new FoamStepData { Utility = "simpleFoam" }]
        };
        data.BaseFiles[0].RelativePath = "../system/controlDict";

        var result = await session.RunAsync(Task(data));

        Assert.That(result.Rejections, Has.Count.EqualTo(2));
        Assert.That(result.Rejections, Has.Some.Contains("foamyHexMesh"));
        Assert.That(result.Rejections, Has.Some.Contains("../system/controlDict"));
    }

    [Test]
    public async Task AFailedSolveIsDataWithTheStepAndTheTailTest()
    {
        var session = RequireSession();
        var data = PitzCase("FAKE-FAIL\n");
        data.ArtifactPolicy = new FoamArtifactPolicyData { Times = FoamArtifactTimes.Latest, PostProcessing = true };

        var result = await session.RunAsync(Task(data));

        Assert.That(result.Rejections, Is.Empty);
        Assert.That(result.FailedStep, Is.EqualTo("blockMesh"));
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.LogTail, Does.Contain("FOAM FATAL ERROR"));
        Assert.That(result.Steps, Has.Count.EqualTo(1));
        Assert.That(result.Converged, Is.False);
        Assert.That(result.ArtifactBlobId, Is.Null, "no artifact of a failed run unless its logs were asked for");
        Assert.That(result.ResponseRow, Is.Null);
    }

    [Test]
    public async Task AFailedRunStillDeliversItsLogsWhenTheyWereAskedForTest()
    {
        var session = RequireSession();
        var data = PitzCase("FAKE-FAIL\n");
        data.ArtifactPolicy = new FoamArtifactPolicyData { Times = FoamArtifactTimes.All, Mesh = true, Logs = true, PostProcessing = true };

        var result = await session.RunAsync(Task(data));

        Assert.That(result.FailedStep, Is.EqualTo("blockMesh"));
        Assert.That(result.ArtifactBlobId, Is.Not.Null, "a diverged run is explained by its logs, not by sixty lines of tail");
        Assert.That(result.ArtifactBytes, Is.GreaterThan(0));
        using var archive = ZipFile.OpenRead(m_blobs.GetStoredPath(result.ArtifactBlobId!.Value));
        var entries = archive.Entries.Select(entry => entry.FullName).ToList();
        Assert.That(entries, Does.Contain("log.blockMesh").And.Contain("system/controlDict").And.Contain("case.foam"));
        Assert.That(entries.Where(entry => char.IsAsciiDigit(entry[0])), Is.Empty, "a logs-only artifact: no time directories even when the policy asked for all of them");
    }

    [Test]
    public void CancellationSurfacesAsCancellationNotAsAFailedVariantTest()
    {
        var session = RequireSession();
        var task = PitzTask("FAKE-HANG\n");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        var stopwatch = Stopwatch.StartNew();

        Assert.CatchAsync<OperationCanceledException>(() => session.RunAsync(task, cts.Token));

        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)), "the hung step outlived its cancellation");
    }

    [Test]
    public async Task AnEmptyArtifactPolicyUploadsNothingTest()
    {
        var session = RequireSession();
        var data = PitzCase("ITERATIONS=1\n");
        data.ArtifactPolicy = null;
        data.Extraction = null;

        var result = await session.RunAsync(Task(data));

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.ArtifactBlobId, Is.Null);
        Assert.That(result.ArtifactBytes, Is.EqualTo(0));
        Assert.That(result.ResponseRow, Is.Not.Null);
        Assert.That(result.ResponseRow!.Values, Is.Empty);
    }

    #endregion
}
