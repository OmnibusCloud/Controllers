using System.IO.Compression;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Mock;
using OutWit.Controller.OpenFOAM.Tests.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Tests.Oracle;

/// <summary>
/// A force measured during the solve against the REAL kit: Couette flow
/// between two cylinders, the inner one turned by a rotating zone (MRF), the
/// torque on it known in closed form. The controller's <c>includeFunc</c>
/// adds the response to the node's copy of controlDict and the solver
/// measures the torque the flow puts on the turning wall; the same response
/// measured after the solve (<c>simpleFoam -postProcess</c>) sees that wall
/// at rest, as OpenFOAM moves it only inside the solve, and misses the
/// torque by an order of magnitude and its sign - the finding the step was
/// made for. The case is the test's own fixture, small enough for every CI
/// kit.
/// </summary>
[TestFixture]
[Category("Kit")]
public class FoamKitSolveFunctionsOracleTests
{
    #region Constants

    private const string KIT_VARIABLE = FoamKitResolver.ENV_KIT_PATH;

    private const string FIXTURE = "CouetteMrf";

    private const double INNER_RADIUS = 1;

    private const double OUTER_RADIUS = 2;

    private const double OMEGA = 1;

    private const double NU = 1;

    private const double DEPTH = 0.1;

    /// <summary>The fixture's mesh: four blocks of 20 x 20 cells.</summary>
    private const long CELLS = 1600;

    /// <summary>
    /// The torque on the inner cylinder, about +z: the flow holds back the
    /// wall that turns it, 4 pi mu omega r1^2 r2^2 / (r2^2 - r1^2) per unit
    /// depth (rho = 1).
    /// </summary>
    private static readonly double TORQUE = -4 * Math.PI * NU * OMEGA * INNER_RADIUS * INNER_RADIUS * OUTER_RADIUS * OUTER_RADIUS
                                            / (OUTER_RADIUS * OUTER_RADIUS - INNER_RADIUS * INNER_RADIUS) * DEPTH;

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
        m_storage = OpenFOAMTestPaths.CreateScratch("foam-oracle-solve-functions");
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
    public async Task ATorqueMeasuredDuringTheSolveIsTheFlowsTest()
    {
        var result = await Run(threads: 1);

        Assert.That(result.Rejections, Is.Empty);
        Assert.That(result.ExitCode, Is.EqualTo(0), result.LogTail);
        Assert.That(result.Steps.Select(step => (step.Utility, step.Ranks)), Is.EqualTo(new[]
        {
            ("blockMesh", 1), ("includeFunc", 0), ("simpleFoam", 1), ("simpleFoam", 1)
        }));
        Assert.That(result.Converged, Is.True);
        Assert.That(Value(result, "torque.moment.total_z"), Is.EqualTo(TORQUE).Within(1).Percent, "the closed form, to the mesh's error");
        Assert.That(Math.Abs(Value(result, "torqueAfter.moment.total_z") - TORQUE), Is.GreaterThan(Math.Abs(TORQUE)),
            "measured after the solve, the turning wall is at rest and the torque is not the flow's");
        Assert.That(result.CellCount, Is.EqualTo(CELLS));

        Assert.That(ArtifactText(result, "system/controlDict"), Does.Contain("    #includeFunc torque\n}"), "the node's copy, as the solver read it");
        Assert.That(ArtifactText(result, "log.includeFunc"), Does.Contain("rotating zone (MRF)"), "the log says what was added and why");
    }

    [Test]
    public async Task ATorqueMeasuredDuringADecomposedSolveIsTheFlowsTest()
    {
        if (!m_kit.SupportsParallel)
            Assert.Ignore("the kit has no MPI launcher on this platform");

        var result = await Run(threads: 2);

        Assert.That(result.Rejections, Is.Empty);
        Assert.That(result.ExitCode, Is.EqualTo(0), result.LogTail);
        Assert.That(result.Steps.Select(step => (step.Utility, step.Ranks)), Is.EqualTo(new[]
        {
            ("blockMesh", 1), ("decomposePar", 1), ("includeFunc", 0), ("simpleFoam", 2), ("reconstructPar", 1)
        }));
        Assert.That(Value(result, "torque.moment.total_z"), Is.EqualTo(TORQUE).Within(1).Percent);
        Assert.That(result.CellCount, Is.EqualTo(CELLS), "the processor meshes, summed");
    }

    #endregion

    #region Tools

    private async Task<FoamResultData> Run(int threads)
    {
        var session = new FoamCaseSession(m_kit, m_blobs, new WitTempStorageDefault(m_storage));
        var result = await session.RunAsync(TaskFor(threads));

        TestContext.Out.WriteLine(result);
        TestContext.Out.WriteLine("steps:     " + string.Join(", ", result.Steps));
        TestContext.Out.WriteLine("responses: " + string.Join(", ", result.ResponseRow?.Values.Select(entry => $"{entry.Name}={entry.Value}") ?? []));
        TestContext.Out.WriteLine($"expected:  torque.moment.total_z={TORQUE}");
        if (result.LogTail != null)
            TestContext.Out.WriteLine(result.LogTail);

        return result;
    }

    private FoamTaskData TaskFor(int threads)
    {
        var fixture = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", FIXTURE);
        var files = Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories)
            .Select(file =>
            {
                var bytes = File.ReadAllBytes(file);
                return new FoamFileRefData
                {
                    RelativePath = Path.GetRelativePath(fixture, file).Replace('\\', '/'),
                    BlobId = m_blobs.AddBytes(bytes),
                    Sha256 = "n/a",
                    Size = bytes.Length
                };
            })
            .ToList();

        var steps = new List<FoamStepData> { new() { Utility = "blockMesh" } };
        if (threads > 1)
            steps.Add(new FoamStepData { Utility = "decomposePar" });
        steps.Add(new FoamStepData { Utility = "includeFunc", Arguments = ["torque"] });
        steps.Add(new FoamStepData { Utility = "simpleFoam", Parallel = threads > 1 });
        if (threads > 1)
            steps.Add(new FoamStepData { Utility = "reconstructPar", Arguments = ["-latestTime"] });
        else
            steps.Add(new FoamStepData { Utility = "simpleFoam", Arguments = ["-postProcess", "-func", "torqueAfter", "-latestTime"] });

        return new FoamTaskData
        {
            VariantIndex = 1,
            Case = new FoamCaseData
            {
                BaseFiles = files,
                Recipe = new FoamRecipeData { Application = "simpleFoam", Steps = steps },
                Threads = threads,
                Extraction = new FoamExtractionRequestData { Responses = threads > 1 ? [Torque("torque")] : [Torque("torque"), Torque("torqueAfter")] },
                ArtifactPolicy = new FoamArtifactPolicyData { Logs = true },
                CellCount = CELLS,
                SolverClass = "incompressible-steady"
            }
        };
    }

    private static FoamResponseSpecData Torque(string name)
    {
        return new FoamResponseSpecData
        {
            Name = name,
            Kind = FoamResponseKind.Forces,
            Patches = ["innerWall"],
            Parameters =
            [
                new FoamNamedValueData { Name = "rho", Value = "rhoInf" },
                new FoamNamedValueData { Name = "rhoInf", Value = "1" },
                new FoamNamedValueData { Name = "CofR", Value = "(0 0 0)" }
            ]
        };
    }

    private static double Value(FoamResultData result, string name)
    {
        var value = result.ResponseRow?.Values.FirstOrDefault(entry => entry.Name == name);
        Assert.That(value, Is.Not.Null, $"the row carries {name}");
        return value!.Value;
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
