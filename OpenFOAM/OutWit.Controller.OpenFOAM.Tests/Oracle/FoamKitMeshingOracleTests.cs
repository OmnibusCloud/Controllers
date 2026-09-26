using System.IO.Compression;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Mock;
using OutWit.Controller.OpenFOAM.Tests.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Tests.Oracle;

/// <summary>
/// Meshing on the decomposed case against the REAL kit: a sphere cut out of
/// a box by snappyHexMesh on two ranks after decomposePar (the order the
/// motorBike tutorial meshes in), the controller's own restore0Dir
/// -processor putting the initial fields into the processor directories, the
/// solver on the snapped mesh. Without the restore the decomposed fields -
/// split over the box before the sphere existed - do not fit the snapped mesh
/// and the solver stops; on a node without MPI the same recipe meshes and
/// solves the whole case serially. The case is the test's own fixture
/// (an analytic sphere, no geometry file), small enough for every CI kit.
/// </summary>
[TestFixture]
[Category("Kit")]
public class FoamKitMeshingOracleTests
{
    #region Constants

    private const string KIT_VARIABLE = FoamKitResolver.ENV_KIT_PATH;

    private const string FIXTURE = "SphereInBox";

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
        m_storage = OpenFOAMTestPaths.CreateScratch("foam-oracle-meshing");
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
    public async Task MeshingOnTheDecomposedCaseRunsOnTheRestoredFieldsTest()
    {
        if (!m_kit.SupportsParallel)
            Assert.Ignore("the kit has no MPI launcher on this platform");

        var result = await Run(threads: 2, restore: true);

        Assert.That(result.Rejections, Is.Empty);
        Assert.That(result.ExitCode, Is.EqualTo(0), result.LogTail);
        Assert.That(result.Steps.Select(step => (step.Utility, step.Ranks)), Is.EqualTo(new[]
        {
            ("blockMesh", 1), ("decomposePar", 1), ("snappyHexMesh", 2), ("restore0Dir", 0), ("simpleFoam", 2),
            ("reconstructParMesh", 1), ("reconstructPar", 1), ("postProcess", 1)
        }));
        Assert.That(result.ResponseRow?.Values.Select(value => value.Name), Has.Some.StartsWith("sphereP."), "the solver ran with a boundary condition on the snapped sphere");
        Assert.That(MeshBoundary(result), Does.Contain("sphere"), "the reconstructed mesh carries the patch snappyHexMesh cut");
    }

    [Test]
    public async Task WithoutTheRestoreTheDecomposedFieldsDoNotFitTheSnappedMeshTest()
    {
        if (!m_kit.SupportsParallel)
            Assert.Ignore("the kit has no MPI launcher on this platform");

        var result = await Run(threads: 2, restore: false);

        Assert.That(result.Rejections, Is.Empty);
        Assert.That(result.ExitCode, Is.Not.EqualTo(0), "the fields decomposePar split over the box know no 'sphere' patch");
        Assert.That(result.FailedStep, Is.EqualTo("simpleFoam"));
        Assert.That(result.LogTail, Does.Contain("Cannot find patchField entry for sphere"), "OpenFOAM's own words for it");
    }

    [Test]
    public async Task OnANodeWithoutMpiTheSameRecipeMeshesAndSolvesTheWholeCaseTest()
    {
        var result = await Run(threads: 1, restore: true);

        Assert.That(result.Rejections, Is.Empty);
        Assert.That(result.ExitCode, Is.EqualTo(0), result.LogTail);
        Assert.That(result.Steps.Where(step => step.Ranks > 0).Select(step => step.Utility), Is.EqualTo(new[] { "blockMesh", "snappyHexMesh", "simpleFoam", "postProcess" }),
            "serially, the decomposition steps are skipped and the restore has nothing to do");
        Assert.That(result.Steps.Single(step => step.Utility == "restore0Dir").ExitCode, Is.EqualTo(0));
        Assert.That(MeshBoundary(result), Does.Contain("sphere"));
    }

    #endregion

    #region Tools

    private async Task<FoamResultData> Run(int threads, bool restore)
    {
        var session = new FoamCaseSession(m_kit, m_blobs, new WitTempStorageDefault(m_storage));
        var result = await session.RunAsync(TaskFor(threads, restore));

        TestContext.Out.WriteLine(result);
        TestContext.Out.WriteLine("steps:     " + string.Join(", ", result.Steps));
        TestContext.Out.WriteLine("responses: " + string.Join(", ", result.ResponseRow?.Values.Select(entry => $"{entry.Name}={entry.Value}") ?? []));
        if (result.LogTail != null)
            TestContext.Out.WriteLine(result.LogTail);

        return result;
    }

    private FoamTaskData TaskFor(int threads, bool restore)
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

        var steps = new List<FoamStepData>
        {
            new() { Utility = "blockMesh" },
            new() { Utility = "decomposePar" },
            new() { Utility = "snappyHexMesh", Arguments = ["-overwrite"], Parallel = true }
        };
        if (restore)
            steps.Add(new FoamStepData { Utility = "restore0Dir", Arguments = ["-processor"] });
        steps.Add(new FoamStepData { Utility = "simpleFoam", Parallel = true });
        steps.Add(new FoamStepData { Utility = "reconstructParMesh", Arguments = ["-constant"] });
        steps.Add(new FoamStepData { Utility = "reconstructPar", Arguments = ["-latestTime"] });
        steps.Add(new FoamStepData { Utility = "postProcess", Arguments = ["-func", "sphereP", "-latestTime"] });

        return new FoamTaskData
        {
            VariantIndex = 1,
            Case = new FoamCaseData
            {
                BaseFiles = files,
                Recipe = new FoamRecipeData { Application = "simpleFoam", MeshesPerVariant = true, Steps = steps },
                Threads = threads,
                Extraction = new FoamExtractionRequestData
                {
                    Responses = [new FoamResponseSpecData { Name = "sphereP", Kind = FoamResponseKind.PatchValue, Patches = ["sphere"], Fields = ["p"], Operation = "areaAverage" }]
                },
                ArtifactPolicy = new FoamArtifactPolicyData { Times = FoamArtifactTimes.Latest, Mesh = true, Logs = true },
                CellCount = 2000,
                SolverClass = "incompressible-steady"
            }
        };
    }

    /// <summary>The mesh's patch list as the result's artifact carries it.</summary>
    private string MeshBoundary(FoamResultData result)
    {
        Assert.That(result.ArtifactBlobId, Is.Not.Null);
        using var archive = ZipFile.OpenRead(m_blobs.GetStoredPath(result.ArtifactBlobId!.Value));
        var boundary = archive.GetEntry("constant/polyMesh/boundary");
        Assert.That(boundary, Is.Not.Null, "the artifact carries the mesh");

        using var reader = new StreamReader(boundary!.Open());
        return reader.ReadToEnd();
    }

    #endregion
}
