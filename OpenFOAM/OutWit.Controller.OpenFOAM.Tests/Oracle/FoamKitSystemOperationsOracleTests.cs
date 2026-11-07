using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Tests.Oracle;

/// <summary>
/// The second line against a case that runs commands on the node, against
/// the REAL kit: whatever a rule misses, a solver process started with the
/// controller's environment and scratch runs with <c>allowSystemOperations</c>
/// off - the kit's own etc/controlDict allows it (measured on v2606,
/// 2026-10-28: a <c>systemCall</c> wrote a file on the node), and the
/// user-level controlDict the scratch carries turns it off. The solver is
/// run directly here, past the content rules, which refuse the case first.
/// </summary>
[TestFixture]
[Category("Kit")]
public class FoamKitSystemOperationsOracleTests
{
    #region Constants

    private const string KIT_VARIABLE = FoamKitResolver.ENV_KIT_PATH;

    private const string CONTROL_DICT =
        "FoamFile { version 2.0; format ascii; class dictionary; object controlDict; }\n" +
        "application icoFoam;\nstartFrom startTime;\nstartTime 0;\nstopAt endTime;\nendTime 0.005;\ndeltaT 0.005;\n" +
        "writeControl timeStep;\nwriteInterval 1;\nwriteFormat ascii;\nwritePrecision 6;\ntimeFormat general;\ntimePrecision 6;\nrunTimeModifiable true;\n" +
        "functions\n{\n    marker\n    {\n        type            systemCall;\n        libs            (utilityFunctionObjects);\n" +
        "        executeCalls    ();\n        writeCalls      ();\n        endCalls        (\"{0}\");\n    }\n}\n";

    private const string BLOCK_MESH_DICT =
        "FoamFile { version 2.0; format ascii; class dictionary; object blockMeshDict; }\n" +
        "scale 0.1;\nvertices ((0 0 0) (1 0 0) (1 1 0) (0 1 0) (0 0 0.1) (1 0 0.1) (1 1 0.1) (0 1 0.1));\n" +
        "blocks (hex (0 1 2 3 4 5 6 7) (4 4 1) simpleGrading (1 1 1));\nedges ();\n" +
        "boundary\n(\n    movingWall { type wall; faces ((3 7 6 2)); }\n    fixedWalls { type wall; faces ((0 4 7 3) (2 6 5 1) (1 5 4 0)); }\n" +
        "    frontAndBack { type empty; faces ((0 3 2 1) (4 5 6 7)); }\n);\nmergePatchPairs ();\n";

    private const string FV_SCHEMES =
        "FoamFile { version 2.0; format ascii; class dictionary; object fvSchemes; }\n" +
        "ddtSchemes { default Euler; }\ngradSchemes { default Gauss linear; }\ndivSchemes { default none; div(phi,U) Gauss linear; }\n" +
        "laplacianSchemes { default Gauss linear orthogonal; }\ninterpolationSchemes { default linear; }\nsnGradSchemes { default orthogonal; }\n";

    private const string FV_SOLUTION =
        "FoamFile { version 2.0; format ascii; class dictionary; object fvSolution; }\n" +
        "solvers\n{\n    p { solver PCG; preconditioner DIC; tolerance 1e-06; relTol 0.05; }\n    pFinal { $p; relTol 0; }\n" +
        "    U { solver smoothSolver; smoother symGaussSeidel; tolerance 1e-05; relTol 0; }\n}\n" +
        "PISO { nCorrectors 2; nNonOrthogonalCorrectors 0; pRefCell 0; pRefValue 0; }\n";

    private const string TRANSPORT = "FoamFile { version 2.0; format ascii; class dictionary; object transportProperties; }\nnu 0.01;\n";

    private const string U =
        "FoamFile { version 2.0; format ascii; class volVectorField; object U; }\ndimensions [0 1 -1 0 0 0 0];\ninternalField uniform (0 0 0);\n" +
        "boundaryField\n{\n    movingWall { type fixedValue; value uniform (1 0 0); }\n    fixedWalls { type noSlip; }\n    frontAndBack { type empty; }\n}\n";

    private const string P =
        "FoamFile { version 2.0; format ascii; class volScalarField; object p; }\ndimensions [0 2 -2 0 0 0 0];\ninternalField uniform 0;\n" +
        "boundaryField\n{\n    movingWall { type zeroGradient; }\n    fixedWalls { type zeroGradient; }\n    frontAndBack { type empty; }\n}\n";

    #endregion

    #region Fields

    private string m_storage = null!;

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
        m_storage = OpenFOAMTestPaths.CreateScratch("foam-oracle-system-operations");
    }

    [TearDown]
    public void TearDown()
    {
        if (m_storage != null)
            OpenFOAMTestPaths.TryDelete(m_storage);
    }

    #region Oracle Tests

    [Test]
    public async Task ASolverStartedByTheControllerRunsNoCommandOfTheCasesTest()
    {
        var temp = new WitTempStorageDefault(m_storage);
        var scratch = FoamScratchPath.CreateScratch(temp, "openfoam");
        try
        {
            var caseDirectory = Path.Combine(scratch.UsablePath, "case");
            var marker = Path.Combine(scratch.UsablePath, "marker.txt").Replace('\\', '/');
            var command = OperatingSystem.IsWindows() ? $"cmd /c echo ran > {marker}" : $"sh -c 'echo ran > {marker}'";
            WriteCase(caseDirectory, command);

            var environment = m_kit.EnvironmentFor(scratch.UsablePath);
            var mesh = await FoamProcessRunner.RunAsync(m_kit.ExecutablePath("blockMesh"), [], caseDirectory, environment, Path.Combine(caseDirectory, "log.blockMesh"));
            Assert.That(mesh.ExitCode, Is.EqualTo(0), mesh.LogTail);

            var solve = await FoamProcessRunner.RunAsync(m_kit.ExecutablePath("icoFoam"), [], caseDirectory, environment, Path.Combine(caseDirectory, "log.icoFoam"));
            var log = await File.ReadAllTextAsync(Path.Combine(caseDirectory, "log.icoFoam"));

            Assert.That(solve.ExitCode, Is.EqualTo(0), solve.LogTail);
            Assert.That(log, Does.Contain("Disallowing user-supplied system call operations"), "the scratch's user-level controlDict overrides the kit's");
            Assert.That(log, Does.Not.Contain("Allowing user-supplied system call operations"));
            Assert.That(File.Exists(marker), Is.False, "the case's command never ran");
        }
        finally
        {
            FoamScratchPath.Delete(temp, scratch);
        }
    }

    #endregion

    #region Tools

    private static void WriteCase(string root, string command)
    {
        Write(root, "system/controlDict", CONTROL_DICT.Replace("{0}", command));
        Write(root, "system/blockMeshDict", BLOCK_MESH_DICT);
        Write(root, "system/fvSchemes", FV_SCHEMES);
        Write(root, "system/fvSolution", FV_SOLUTION);
        Write(root, "constant/transportProperties", TRANSPORT);
        Write(root, "0/U", U);
        Write(root, "0/p", P);
    }

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    #endregion
}
