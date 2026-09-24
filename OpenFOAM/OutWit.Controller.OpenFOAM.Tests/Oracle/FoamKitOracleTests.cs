using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Mock;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Oracle;

/// <summary>
/// The controller against the REAL kit: pitzDaily from the kit's own
/// tutorials through FoamCaseSession, serially and on two ranks, with
/// responses read back. Runs only where a kit is present - the OUTWIT_OPENFOAM
/// variable names the kit folder (the one holding KIT.env), the same override
/// the resolver honours - and is skipped everywhere else, so the ordinary
/// test run never needs 500 MB of solver. The numbers asserted are the ones
/// the kit's acceptance recorded (pitzDaily converges at iteration 281
/// serially and 288-289 on four ranks, when the kit was accepted).
/// </summary>
[TestFixture]
[Category("Kit")]
public class FoamKitOracleTests
{
    private const string KIT_VARIABLE = FoamKitResolver.ENV_KIT_PATH;

    private string m_storage = null!;
    private FoamTestBlobService m_blobs = null!;
    private FoamKit m_kit = null!;

    [SetUp]
    public void Setup()
    {
        var kitPath = Environment.GetEnvironmentVariable(KIT_VARIABLE);
        if (string.IsNullOrEmpty(kitPath))
            Assert.Ignore($"{KIT_VARIABLE} is not set; the kit oracle runs only where a kit is unpacked");

        var kit = FoamKitResolver.Resolve(typeof(FoamKit).Assembly.Location)
                  ?? throw new InvalidOperationException($"{KIT_VARIABLE}={kitPath} does not resolve to a kit");
        m_kit = kit;

        m_storage = OpenFOAMTestPaths.CreateScratch("foam-oracle-blobs");
        m_blobs = new FoamTestBlobService(m_storage);
    }

    [TearDown]
    public void TearDown()
    {
        if (m_storage != null)
            OpenFOAMTestPaths.TryDelete(m_storage);
    }

    #region Tools

    private FoamTaskData PitzDaily(int threads, bool parallel)
    {
        var tutorial = Path.Combine(m_kit.Environment.Get("FOAM_TUTORIALS", m_kit.Root)!, "incompressible", "simpleFoam", "pitzDaily");
        Assert.That(Directory.Exists(tutorial), Is.True, $"the kit carries no pitzDaily at {tutorial}");

        var files = new List<FoamFileRefData>();
        foreach (var file in Directory.EnumerateFiles(tutorial, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(tutorial, file).Replace('\\', '/');
            if (relative.StartsWith("Allrun", StringComparison.Ordinal) || relative.StartsWith("Allclean", StringComparison.Ordinal))
                continue;

            var text = File.ReadAllText(file);
            // The inlet velocity becomes the sweep's parameter.
            var templated = relative == "0/U";
            if (templated)
                text = text.Replace("uniform (10 0 0)", "uniform ({{oc1}} 0 0)");

            files.Add(new FoamFileRefData
            {
                RelativePath = relative,
                BlobId = m_blobs.AddText(text),
                Sha256 = "n/a",
                Size = text.Length,
                Templated = templated
            });
        }

        Assert.That(files.Any(file => file.RelativePath == "0/U" && file.Templated), Is.True, "0/U carries the inlet velocity");

        var steps = new List<FoamStepData> { new() { Utility = "blockMesh" }, new() { Utility = "checkMesh" } };
        if (parallel)
            steps.Add(new FoamStepData { Utility = "decomposePar" });
        steps.Add(new FoamStepData { Utility = "simpleFoam", Parallel = parallel });
        if (parallel)
            steps.Add(new FoamStepData { Utility = "reconstructPar", Arguments = ["-latestTime"] });
        steps.Add(new FoamStepData { Utility = "postProcess", Arguments = ["-func", "inletP", "-latestTime"] });
        steps.Add(new FoamStepData { Utility = "postProcess", Arguments = ["-func", "pRange", "-latestTime"] });

        return new FoamTaskData
        {
            VariantIndex = 1,
            BaseFiles = files,
            Substitutions = [new FoamTokenValueData { Token = "{{oc1}}", Value = "10" }],
            Recipe = new FoamRecipeData { Application = "simpleFoam", Steps = steps },
            Threads = threads,
            Extraction = new FoamExtractionRequestData
            {
                Responses =
                [
                    // The inlet: the outlet holds p = 0 by its boundary condition, which would compare 0 with 0.
                    new FoamResponseSpecData { Name = "inletP", Kind = FoamResponseKind.PatchValue, Patches = ["inlet"], Fields = ["p"], Operation = "areaAverage" },
                    new FoamResponseSpecData { Name = "pRange", Kind = FoamResponseKind.FieldMinMax, Fields = ["p", "U"] }
                ]
            },
            ArtifactPolicy = new FoamArtifactPolicyData { Times = FoamArtifactTimes.Latest, Mesh = true, Logs = true },
            CellCount = 12225,
            SolverClass = "incompressible-steady"
        };
    }

    /// <summary>
    /// The first response value whose name starts with the prefix. The column
    /// names are the function objects' own (<c>areaAverage(p)</c>,
    /// <c>min(p)</c> ...), so a test asks by response and column fragment.
    /// </summary>
    private static double Value(FoamResultData result, string prefix, string fragment = "")
    {
        var names = string.Join(", ", result.ResponseRow?.Values.Select(entry => entry.Name) ?? []);
        var value = result.ResponseRow?.Values.FirstOrDefault(entry => entry.Name.StartsWith(prefix, StringComparison.Ordinal) && entry.Name.Contains(fragment, StringComparison.Ordinal));
        Assert.That(value, Is.Not.Null, $"the response row carries no '{prefix}*{fragment}*': [{names}]");
        return value!.Value;
    }

    private static void PrintRow(FoamResultData result)
    {
        TestContext.Out.WriteLine(result);
        TestContext.Out.WriteLine("responses: " + string.Join(", ", result.ResponseRow?.Values.Select(entry => $"{entry.Name}={entry.Value}") ?? []));
        TestContext.Out.WriteLine("residuals: " + string.Join(", ", result.FinalResiduals.Select(entry => $"{entry.Name}={entry.Value}")));
        TestContext.Out.WriteLine("steps:     " + string.Join(", ", result.Steps));
        if (result.Rejections.Count > 0)
            TestContext.Out.WriteLine("rejections: " + string.Join(" | ", result.Rejections));
        if (result.LogTail != null)
            TestContext.Out.WriteLine(result.LogTail);
    }

    #endregion

    #region Oracle Tests

    [Test]
    public async Task PitzDailyRunsSeriallyThroughTheControllerTest()
    {
        var session = new FoamCaseSession(m_kit, m_blobs);

        var result = await session.RunAsync(PitzDaily(threads: 1, parallel: false));
        PrintRow(result);

        Assert.That(result.Rejections, Is.Empty);
        Assert.That(result.ExitCode, Is.EqualTo(0), result.LogTail);
        Assert.That(result.Converged, Is.True);
        Assert.That(result.Iterations, Is.InRange(270, 300), "pitzDaily converges at 281 iterations serially on the kit's acceptance");
        Assert.That(result.CellCount, Is.EqualTo(12225));
        Assert.That(result.CheckMeshVerdict, Is.EqualTo("ok"));
        Assert.That(result.Steps.Select(step => step.Utility), Is.EqualTo(new[] { "blockMesh", "checkMesh", "simpleFoam", "postProcess", "postProcess" }));
        Assert.That(result.FinalResiduals.Select(residual => residual.Name), Does.Contain("residual.p").And.Contain("residual.Ux"));

        Value(result, "inletP.");
        Assert.That(Value(result, "pRange.", "max"), Is.GreaterThan(Value(result, "pRange.", "min")));

        Assert.That(result.ArtifactBlobId, Is.Not.Null);
        Assert.That(result.ArtifactBytes, Is.GreaterThan(100_000));
    }

    [Test]
    public async Task PitzDailyRunsOnTwoRanksAndAgreesWithTheSerialRunTest()
    {
        if (!m_kit.SupportsParallel)
            Assert.Ignore("the kit has no MPI launcher on this platform");

        var session = new FoamCaseSession(m_kit, m_blobs);

        var serial = await session.RunAsync(PitzDaily(threads: 1, parallel: false));
        var parallel = await session.RunAsync(PitzDaily(threads: 2, parallel: true));
        PrintRow(serial);
        PrintRow(parallel);

        Assert.That(parallel.Rejections, Is.Empty);
        Assert.That(parallel.ExitCode, Is.EqualTo(0), parallel.LogTail);
        Assert.That(parallel.Steps.Single(step => step.Utility == "simpleFoam").Ranks, Is.EqualTo(2));
        Assert.That(parallel.Converged, Is.True);

        // The determinism tolerance: a residual-controlled run stops
        // at a slightly different iteration per decomposition; the converged
        // responses agree to engineering tolerance.
        var serialP = Value(serial, "inletP.");
        var parallelP = Value(parallel, "inletP.");
        TestContext.Out.WriteLine($"inlet p: serial {serialP}, parallel {parallelP}, iterations {serial.Iterations} vs {parallel.Iterations}");
        Assert.That(parallelP, Is.EqualTo(serialP).Within(2).Percent);
        Assert.That(Value(parallel, "pRange.", "max"), Is.EqualTo(Value(serial, "pRange.", "max")).Within(2).Percent);
    }

    [Test]
    public async Task ADivergingVariantComesBackAsAFailedResultTest()
    {
        var session = new FoamCaseSession(m_kit, m_blobs);
        var task = PitzDaily(threads: 1, parallel: false);
        task.Substitutions[0].Value = "1e6";

        var result = await session.RunAsync(task);
        PrintRow(result);

        Assert.That(result.Rejections, Is.Empty);
        Assert.That(result.ExitCode, Is.Not.EqualTo(0), "an inlet at 1e6 m/s under FOAM_SIGFPE is a crash, not a result");
        Assert.That(result.FailedStep, Is.EqualTo("simpleFoam"));
        Assert.That(result.Converged, Is.False);
        Assert.That(result.ArtifactBlobId, Is.Null);
    }

    #endregion
}
