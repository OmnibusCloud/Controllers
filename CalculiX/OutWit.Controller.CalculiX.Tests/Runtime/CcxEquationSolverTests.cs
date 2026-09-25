using OutWit.Controller.CalculiX.Runtime;

namespace OutWit.Controller.CalculiX.Tests.Runtime;

/// <summary>
/// Multithreaded SPOOLES returns a wrong field now and then, with exit 0: a solve that may go
/// through SPOOLES gets one equation-solver thread, a PARDISO solve keeps its threads.
/// </summary>
[TestFixture]
public class CcxEquationSolverTests
{
    private string m_directory = null!;

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"ccx-solver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(m_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    #region Solver Tests

    [Test]
    public void DefaultSolveWherePardisoIsLinkedKeepsItsThreadsTest()
    {
        Assert.That(CcxEquationSolver.UsesSpooles(["*STEP", "*STATIC", "*END STEP"], pardisoLinked: true), Is.False);
        Assert.That(CcxEquationSolver.UsesSpooles(["*STEP", "*STATIC, SOLVER=PARDISO", "*END STEP"], pardisoLinked: true), Is.False);
    }

    [Test]
    public void ExplicitSpoolesIsSeenInAnySpellingTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CcxEquationSolver.UsesSpooles(["*STATIC, SOLVER=SPOOLES"], pardisoLinked: true), Is.True);
            Assert.That(CcxEquationSolver.UsesSpooles(["*Static, Solver = Spooles"], pardisoLinked: true), Is.True);
            Assert.That(CcxEquationSolver.UsesSpooles(["*FREQUENCY,SOLVER=spooles", "10"], pardisoLinked: true), Is.True);
            Assert.That(CcxEquationSolver.UsesSpooles(["*HEAT TRANSFER, STEADY STATE,", "SOLVER=SPOOLES"], pardisoLinked: true), Is.True,
                "a keyword card continued on the next line");
        });
    }

    [Test]
    public void WithoutPardisoEverySolveIsSpoolesTest()
    {
        Assert.That(CcxEquationSolver.UsesSpooles(["*STATIC"], pardisoLinked: false), Is.True,
            "ccx falls back to SPOOLES where PARDISO is not linked (the macOS kit)");
        Assert.That(CcxEquationSolver.PardisoLinked(isMacOS: true), Is.False);
        Assert.That(CcxEquationSolver.PardisoLinked(isMacOS: false), Is.True);
    }

    [Test]
    public void ThreadsForReadsTheDeckOnDiskTest()
    {
        var spooles = Path.Combine(m_directory, "spooles.inp");
        File.WriteAllText(spooles, "*STEP\n*STATIC, SOLVER=SPOOLES\n*END STEP\n");
        var plain = Path.Combine(m_directory, "plain.inp");
        File.WriteAllText(plain, "*STEP\n*STATIC\n*END STEP\n");

        Assert.That(CcxEquationSolver.ThreadsFor(spooles), Is.EqualTo(CcxEquationSolver.SPOOLES_THREADS));
        Assert.That(CcxEquationSolver.ThreadsFor(plain), OperatingSystem.IsMacOS() ? Is.EqualTo(CcxEquationSolver.SPOOLES_THREADS) : Is.Null);
    }

    #endregion

    #region Start Info Tests

    [Test]
    public void TheEquationSolverThreadsReachTheProcessTest()
    {
        var startInfo = CcxProcessRunner.CreateStartInfo("ccx", "job", m_directory, 8, CcxEquationSolver.SPOOLES_THREADS);

        Assert.That(startInfo.EnvironmentVariables["OMP_NUM_THREADS"], Is.EqualTo("8"), "assembly and stress recovery keep their threads");
        Assert.That(startInfo.EnvironmentVariables[CcxEquationSolver.THREADS_VARIABLE], Is.EqualTo("1"));
    }

    [Test]
    public void WithoutEquationSolverThreadsTheVariableIsNotInheritedTest()
    {
        Environment.SetEnvironmentVariable(CcxEquationSolver.THREADS_VARIABLE, "32");
        try
        {
            var startInfo = CcxProcessRunner.CreateStartInfo("ccx", "job", m_directory, 8);

            Assert.That(startInfo.EnvironmentVariables.ContainsKey(CcxEquationSolver.THREADS_VARIABLE), Is.False,
                "a value on the node must not decide the solve's threads");
        }
        finally
        {
            Environment.SetEnvironmentVariable(CcxEquationSolver.THREADS_VARIABLE, null);
        }
    }

    #endregion
}
