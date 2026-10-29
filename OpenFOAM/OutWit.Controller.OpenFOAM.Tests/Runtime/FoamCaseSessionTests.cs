using System.Diagnostics;
using System.IO.Compression;
using OutWit.Controller.OpenFOAM.Extraction;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Mock;
using OutWit.Controller.OpenFOAM.Tests.Utils;

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
    private RecordingTempStorage? m_temp;

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

    private FoamCaseSession RequireSession(params string[] utilities)
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

        m_kit = FakeKit.Create(fakeFoam, ["blockMesh", "simpleFoam", "checkMesh", .. utilities]);
        m_temp = new RecordingTempStorage(m_storage);
        return new FoamCaseSession(m_kit.Resolve(), m_blobs, m_temp);
    }

    private void AssertScratchGivenBack()
    {
        var temp = m_temp ?? throw new InvalidOperationException("No session was made.");

        Assert.That(temp.CreatedScopes, Has.Count.EqualTo(1), "one run, one scope");
        Assert.That(temp.DeletedScopes, Is.EqualTo(temp.CreatedScopes), "the scope goes back through the host's temp folder");
        Assert.That(Directory.Exists(temp.CreatedScopes[0]), Is.False, "nothing of the run is left behind");
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
            Extraction = new FoamExtractionRequestData { Responses = [new FoamResponseSpecData { Name = "coeffs", Kind = FoamResponseKind.ForceCoeffs, Patches = ["wall"], Parameters = [new FoamNamedValueData { Name = "CofR", Value = "(0 0 0)" }, new FoamNamedValueData { Name = "liftDir", Value = "(0 0 1)" }, new FoamNamedValueData { Name = "dragDir", Value = "(1 0 0)" }, new FoamNamedValueData { Name = "magUInf", Value = "20" }, new FoamNamedValueData { Name = "lRef", Value = "1" }, new FoamNamedValueData { Name = "Aref", Value = "1" }, new FoamNamedValueData { Name = "rhoInf", Value = "1" }] }] },
            ArtifactPolicy = new FoamArtifactPolicyData { Times = FoamArtifactTimes.Latest, Logs = true, PostProcessing = true },
            CellCount = 12225,
            SolverClass = "incompressible-steady"
        };
    }

    private FoamFileRefData BlobFile(string relativePath, string text, bool templated = false)
    {
        return m_blobs.AddFile(relativePath, text, templated);
    }

    /// <summary>The notes a result carries in its log tail, as kind, column and text.</summary>
    private static IEnumerable<(FoamNoteKind Kind, string? Column, string Text)> NotesOf(FoamResultData result)
    {
        return FoamNoteLog.Parse(result.LogTail).Notes.Select(note => (note.Kind, note.Column, note.Text));
    }

    /// <summary>A file of the result's artifact, as text.</summary>
    private string ArtifactText(FoamResultData result, string entryName)
    {
        using var archive = ZipFile.OpenRead(m_blobs.GetStoredPath(result.ArtifactBlobId!.Value));
        using var reader = new StreamReader(archive.GetEntry(entryName)!.Open());
        return reader.ReadToEnd();
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
    public async Task TheCellCountIsTheLastOneAStepReportsTest()
    {
        var session = RequireSession();

        var result = await session.RunAsync(PitzTask("ITERATIONS=1\nCELLS.blockMesh=5000\nCELLS.checkMesh=185237\nCELLS.simpleFoam=0\n"));

        Assert.That(result.ExitCode, Is.EqualTo(0), result.LogTail);
        Assert.That(result.CellCount, Is.EqualTo(185237), "the background mesh blockMesh reports is not the mesh a later step made");
    }

    [Test]
    public async Task TheCellCountIsTheMeshTheRunEndedWithTest()
    {
        var session = RequireSession();
        var data = PitzCase("ITERATIONS=1\nCELLS.blockMesh=5000\n");
        data.BaseFiles.Add(BlobFile("constant/polyMesh/owner",
            "FoamFile\n{\n    format      ascii;\n    class       labelList;\n" +
            "    note        \"nPoints:218801  nCells:185237  nFaces:589238  nInternalFaces:520000\";\n    object      owner;\n}\n0\n(\n)\n"));

        var result = await session.RunAsync(Task(data));

        Assert.That(result.ExitCode, Is.EqualTo(0), result.LogTail);
        Assert.That(result.CellCount, Is.EqualTo(185237), "the mesh on disk outranks what a log said on the way");
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
    public async Task ASweptReferenceValueOfTheCasesOwnForceReachesTheReMeasuredResponseTest()
    {
        // The case's forceCoeffs1 sweeps its lRef; the initiator offered it as
        // forceCoeffs1_sweep with lRef as written. Before: the node wrote the
        // response with lRef 1 in every variant (K4).
        var session = RequireSession();
        var data = PitzCase("ITERATIONS=1\n");
        data.BaseFiles.RemoveAll(file => file.RelativePath == "system/controlDict");
        data.BaseFiles.Add(BlobFile("system/controlDict",
            "FoamFile { object controlDict; }\napplication simpleFoam;\nendTime 5;\nfunctions\n{\n" +
            "    forceCoeffs1 { type forceCoeffs; libs (forces); patches (wall); rho rhoInf; rhoInf 1; CofR (0 0 0); liftDir (0 0 1); dragDir (1 0 0); magUInf 20; lRef {{oc2}}; Aref 1; }\n}\n", templated: true));
        data.Extraction!.Responses[0].Name = "forceCoeffs1_sweep";
        data.Recipe!.Steps.Insert(2, new FoamStepData { Utility = "includeFunc", Arguments = ["forceCoeffs1_sweep"] });

        var result = await session.RunAsync(Task(data));

        Assert.That(result.Rejections, Is.Empty);
        Assert.That(result.ExitCode, Is.EqualTo(0), result.LogTail);
        using var archive = ZipFile.OpenRead(m_blobs.GetStoredPath(result.ArtifactBlobId!.Value));
        using var written = new StreamReader(archive.GetEntry("system/forceCoeffs1_sweep")!.Open());
        Assert.That(await written.ReadToEndAsync(), Does.Contain("lRef            10;"), "the variant's value, not the offered one");
        using var notes = new StreamReader(archive.GetEntry("log.responses")!.Open());
        Assert.That(await notes.ReadToEndAsync(), Does.Contain("Response 'forceCoeffs1_sweep': lRef follows the case's forceCoeffs1 (system/controlDict:6), which the study sweeps - 10 in this variant."));

        // The same sentence reaches the app: an INFO about the whole response.
        Assert.That(NotesOf(result), Is.EqualTo(new[]
        {
            (FoamNoteKind.Info, (string?)null, "Response 'forceCoeffs1_sweep': lRef follows the case's forceCoeffs1 (system/controlDict:6), which the study sweeps - 10 in this variant.")
        }));
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
        Assert.That(result.LogTail, Is.Null, "a refused variant's tail stays empty: its rejections say why");
    }

    [Test]
    public async Task ABlobThatIsNotWhatTheTaskDescribesRefusesTheVariantBeforeAnythingRunsTest()
    {
        // The case changed between the reading that made the study and the
        // upload: the task describes the old controlDict, the blob holds the new.
        var session = RequireSession();
        var data = PitzCase("ITERATIONS=1\n");
        var controlDict = data.BaseFiles.Single(file => file.RelativePath == "system/controlDict");
        File.WriteAllText(m_blobs.GetStoredPath(controlDict.BlobId), "FoamFile { object controlDict; }\napplication simpleFoam;\nendTime 500;\n");

        var result = await session.RunAsync(Task(data));

        Assert.That(result.Rejections, Has.Exactly(1).Items);
        Assert.That(result.Rejections[0], Does.StartWith("system/controlDict: the blob's bytes are not the ones the task describes (size ")
            .And.EndWith("- the case changed after it was read; open it again and submit it again."));
        Assert.That(result.Steps, Is.Empty, "a refused variant, not a failed task");
        AssertScratchGivenBack();
    }

    [Test]
    public async Task ACaseWithAFunctionObjectThatReachesTheNodeIsRefusedBeforeAnythingRunsTest()
    {
        var session = RequireSession();
        var data = PitzCase("ITERATIONS=1\n");
        data.BaseFiles.RemoveAll(file => file.RelativePath == "system/controlDict");
        data.BaseFiles.Add(BlobFile("system/controlDict",
            "FoamFile { object controlDict; }\napplication simpleFoam;\nendTime 5;\n" +
            "functions\n{\n    marker { type systemCall; libs (utilityFunctionObjects); endCalls (\"echo ran\"); }\n}\n"));

        var result = await session.RunAsync(Task(data));

        // The fake kit carries no utilityFunctionObjects either: two refusals,
        // the type's among them, and nothing runs.
        Assert.That(result.Rejections, Has.Some.StartsWith("system/controlDict:6:").And.Some.Contains("'marker' is of type 'systemCall'"));
        Assert.That(result.Steps, Is.Empty);
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
    public async Task AVariantWithTwoValuesForATokenIsRefusedBeforeAnythingRunsTest()
    {
        var session = RequireSession();
        var task = Task(PitzCase("ITERATIONS=1\n"),
            new FoamTokenValueData { Token = "{{oc1}}", Value = "1e-05" },
            new FoamTokenValueData { Token = "{{oc2}}", Value = "10" },
            new FoamTokenValueData { Token = "{{oc2}}", Value = "20" });

        var result = await session.RunAsync(task);

        Assert.That(result.Rejections, Is.EqualTo(new[] { "Token {{oc2}} has more than one value in this variant." }));
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

    #region Note Tests

    [Test]
    public async Task AProbeOutsideTheMeshIsAWarningOnEveryColumnItLeavesEmptyTest()
    {
        var session = RequireSession();
        var data = PitzCase("ITERATIONS=3\nFAKE-PROBES=wake\n");
        data.Extraction = new FoamExtractionRequestData
        {
            Responses =
            [
                new FoamResponseSpecData
                {
                    Name = "wake", Kind = FoamResponseKind.Probe, Fields = ["p", "U"],
                    Parameters = [new FoamNamedValueData { Name = "probeLocations", Value = "((0.1 0 0) (9 9 9))" }]
                }
            ]
        };

        var result = await session.RunAsync(Task(data));

        Assert.That(result.ExitCode, Is.EqualTo(0), result.LogTail);
        Assert.That(result.ResponseRow!.Values.Select(value => value.Name), Is.EqualTo(new[]
        {
            "wake.U@probe0(0.1 0 0)_x", "wake.U@probe0(0.1 0 0)_y", "wake.U@probe0(0.1 0 0)_z", "wake.p@probe0(0.1 0 0)"
        }));
        Assert.That(result.LogTail, Does.StartWith(FoamNoteLog.SEPARATOR + "\n"), "a successful run has no solver tail: the notes come first");
        Assert.That(NotesOf(result), Is.EqualTo(new[]
        {
            (FoamNoteKind.Warning, (string?)"wake.U@probe1(9 9 9)_x", "wake: probe 1 (9 9 9) of U is outside the mesh - not reported."),
            (FoamNoteKind.Warning, (string?)"wake.U@probe1(9 9 9)_y", "wake: probe 1 (9 9 9) of U is outside the mesh - not reported."),
            (FoamNoteKind.Warning, (string?)"wake.U@probe1(9 9 9)_z", "wake: probe 1 (9 9 9) of U is outside the mesh - not reported."),
            (FoamNoteKind.Warning, (string?)"wake.p@probe1(9 9 9)", "wake: probe 1 (9 9 9) of p is outside the mesh - not reported.")
        }), "every empty cell is named as the row names the probe it found");
        Assert.That(ArtifactText(result, "log.responses").Replace("\r\n", "\n"), Is.EqualTo(
            "wake: probe 1 (9 9 9) of U is outside the mesh - not reported.\nwake: probe 1 (9 9 9) of p is outside the mesh - not reported.\n"),
            "log.responses is written as before: one line per sentence");
    }

    [Test]
    public async Task AFunctionObjectThatStoppedWritingIsAWarningOnEveryColumnTest()
    {
        var session = RequireSession();

        var result = await session.RunAsync(PitzTask("ITERATIONS=4\nFAKE-STALE-COEFFS\n"));

        Assert.That(result.ExitCode, Is.EqualTo(0), result.LogTail);
        Assert.That(result.ResponseRow!.Values, Is.Empty, "a stale value is worse than none");
        Assert.That(NotesOf(result), Is.EqualTo(new[]
        {
            (FoamNoteKind.Warning, (string?)"coeffs.Cd", "coeffs: the last row of coefficient.dat is at time 3, the run ended at 4 - not reported."),
            (FoamNoteKind.Warning, (string?)"coeffs.Cl", "coeffs: the last row of coefficient.dat is at time 3, the run ended at 4 - not reported."),
            (FoamNoteKind.Warning, (string?)"coeffs.CmPitch", "coeffs: the last row of coefficient.dat is at time 3, the run ended at 4 - not reported.")
        }));
    }

    [Test]
    public async Task RanksWithoutAParallelStepAreAnInfoOfTheVariantTest()
    {
        var session = RequireSession();
        var data = PitzCase("FAKE-COEFFS\nITERATIONS=2\n");
        data.Recipe!.Steps[2].Parallel = false;

        var result = await session.RunAsync(Task(data));

        Assert.That(result.ExitCode, Is.EqualTo(0), result.LogTail);
        Assert.That(result.ResponseRow!.Values, Is.Not.Empty, "the values stand");
        Assert.That(NotesOf(result), Is.EqualTo(new[]
        {
            (FoamNoteKind.Info, (string?)null, "The task asks for 2 ranks, and no step of the recipe runs in parallel: every step runs serially on one rank, and the other 1 do nothing. Mark the solve (and the steps that may) parallel, or ask for one thread.")
        }));
    }

    [Test]
    public async Task ASetFieldsOverASweptFieldIsAWarningOfTheVariantTest()
    {
        var session = RequireSession("setFields");
        var data = PitzCase("FAKE-COEFFS\nITERATIONS=2\n");
        data.BaseFiles.Add(BlobFile("system/setFieldsDict", "defaultFieldValues ( volVectorFieldValue U (0 0 0) );\nregions ();\n"));
        data.Recipe!.Steps.Insert(2, new FoamStepData { Utility = "setFields" });

        var result = await session.RunAsync(Task(data));

        Assert.That(result.Rejections, Is.Empty);
        Assert.That(result.ExitCode, Is.EqualTo(0), result.LogTail);
        Assert.That(NotesOf(result), Is.EqualTo(new[]
        {
            (FoamNoteKind.Warning, (string?)null, "0/U: internalField carries {{oc2}}, which the study sweeps, and the recipe's setFields step overwrites it everywhere (system/setFieldsDict defaultFieldValues names U): the swept value has no effect.")
        }));
    }

    [Test]
    public async Task APostStepRecordedButNotRunIsAnInfoTest()
    {
        var session = RequireSession("postProcess");
        var data = PitzCase("FAKE-COEFFS\nITERATIONS=2\n");
        data.Extraction!.Responses.Add(new FoamResponseSpecData { Name = "inletP", Kind = FoamResponseKind.PatchValue, Patches = ["inlet"], Fields = ["p"], Operation = "areaAverage" });
        data.Recipe!.Steps.Add(new FoamStepData { Utility = "postProcess", Arguments = ["-func", "inletP", "-latestTime"] });

        var result = await session.RunAsync(Task(data));

        Assert.That(result.Rejections, Is.Empty);
        Assert.That(result.ExitCode, Is.EqualTo(0), result.LogTail);
        Assert.That(result.Steps.Select(step => (step.Utility, step.Ranks)).Last(), Is.EqualTo(("postProcess", 0)), "recorded like any step");
        Assert.That(NotesOf(result), Is.EqualTo(new[]
        {
            (FoamNoteKind.Info, (string?)null, "Not run: 'postProcess -func inletP -latestTime'. The solver measured 'inletP' at every step (the controller's includeFunc), so its last row is the final time's; a post step reads the latest written time only, and for a probe it overwrites the solve's rows with that one.")
        }), "nothing else in the result says the step did nothing");
        Assert.That(ArtifactText(result, "log.postProcess").TrimEnd(), Is.EqualTo(NotesOf(result).Single().Text), "the step's log says the same");
    }

    [Test]
    public async Task AVariantWithoutNotesKeepsTheTailItHadTest()
    {
        var session = RequireSession();

        var succeeded = await session.RunAsync(PitzTask("FAKE-COEFFS\nITERATIONS=2\n"));
        var failed = await session.RunAsync(PitzTask("FAKE-FAIL\n"));

        Assert.That(succeeded.ExitCode, Is.EqualTo(0));
        Assert.That(succeeded.LogTail, Is.Null, "a successful run without notes: no tail, as before");
        Assert.That(failed.FailedStep, Is.EqualTo("blockMesh"));
        Assert.That(failed.LogTail, Does.Contain("FOAM FATAL ERROR").And.Not.Contain(FoamNoteLog.SEPARATOR), "the failed step's tail, nothing after it");

        var (tail, notes) = FoamNoteLog.Parse(failed.LogTail);
        Assert.That(tail, Is.EqualTo(failed.LogTail), "a tail without the separator is the solver's tail whole");
        Assert.That(notes, Is.Empty, "and carries no notes");
    }

    [Test]
    public async Task AFailedRunCarriesItsTailFirstAndItsNotesAfterTest()
    {
        var session = RequireSession();
        var data = PitzCase("FAKE-FAIL\n");
        data.Recipe!.Steps[2].Parallel = false;

        var result = await session.RunAsync(Task(data));

        Assert.That(result.FailedStep, Is.EqualTo("blockMesh"));
        var (tail, notes) = FoamNoteLog.Parse(result.LogTail);
        Assert.That(tail, Does.Contain("FOAM FATAL ERROR"), "the solver's own words first");
        Assert.That(result.LogTail, Does.StartWith(tail!));
        Assert.That(notes.Select(note => (note.Kind, note.Column)), Is.EqualTo(new[] { (FoamNoteKind.Info, (string?)null) }));
        Assert.That(notes[0].Text, Does.StartWith("The task asks for 2 ranks"));
    }

    [Test]
    public async Task ARefusedVariantCarriesNoNotesTest()
    {
        // The reference value follows before the refusal is known: a run
        // that never was says nothing beyond its rejections.
        var session = RequireSession();
        var data = PitzCase("ITERATIONS=1\n");
        data.BaseFiles.RemoveAll(file => file.RelativePath == "system/controlDict");
        data.BaseFiles.Add(BlobFile("system/controlDict",
            "FoamFile { object controlDict; }\napplication simpleFoam;\nendTime 5;\nfunctions\n{\n" +
            "    forceCoeffs1 { type forceCoeffs; libs (forces); patches (wall); rho rhoInf; rhoInf 1; CofR (0 0 0); liftDir (0 0 1); dragDir (1 0 0); magUInf 20; lRef {{oc2}}; Aref 1; }\n}\n", templated: true));
        data.Extraction!.Responses[0].Name = "forceCoeffs1_sweep";
        data.BaseFiles.Add(BlobFile("0/p", "internalField #codeStream { code #{ os << 0; #}; };\n"));

        var result = await session.RunAsync(Task(data));

        Assert.That(result.Rejections, Is.Not.Empty);
        Assert.That(result.LogTail, Is.Null);
    }

    #endregion

    #region Cleanup Tests

    [Test]
    public async Task ACompletedRunGivesItsScratchBackTest()
    {
        var session = RequireSession();

        var result = await session.RunAsync(PitzTask("FAKE-COEFFS\nITERATIONS=2\n"));

        Assert.That(result.ExitCode, Is.EqualTo(0));
        AssertScratchGivenBack();
    }

    [Test]
    public async Task ARefusedVariantGivesItsScratchBackTest()
    {
        var session = RequireSession();
        var data = PitzCase("ITERATIONS=1\n");
        data.BaseFiles.Add(BlobFile("0/p", "internalField #codeStream { code #{ os << 0; #}; };\n"));

        var result = await session.RunAsync(Task(data));

        Assert.That(result.Rejections, Is.Not.Empty);
        AssertScratchGivenBack();
    }

    [Test]
    public async Task AFailedRunGivesItsScratchBackTest()
    {
        var session = RequireSession();

        var result = await session.RunAsync(PitzTask("FAKE-FAIL\n"));

        Assert.That(result.FailedStep, Is.EqualTo("blockMesh"));
        AssertScratchGivenBack();
    }

    [Test]
    public void ACancelledRunGivesItsScratchBackTest()
    {
        var session = RequireSession();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));

        Assert.CatchAsync<OperationCanceledException>(() => session.RunAsync(PitzTask("FAKE-HANG\n"), cts.Token));

        AssertScratchGivenBack();
    }

    [Test]
    public void AnInfrastructureFailureGivesTheScratchBackTest()
    {
        var session = RequireSession();
        var data = PitzCase("ITERATIONS=1\n");
        data.BaseFiles.Add(new FoamFileRefData { RelativePath = "constant/g", BlobId = Guid.NewGuid(), Sha256 = "n/a", Size = 1 });

        Assert.ThrowsAsync<FileNotFoundException>(() => session.RunAsync(Task(data)), "a blob the node cannot fetch is an infrastructure failure");

        AssertScratchGivenBack();
    }

    #endregion
}
