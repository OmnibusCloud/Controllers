using OutWit.Controller.OpenFOAM.Extraction;

namespace OutWit.Controller.OpenFOAM.Tests.Extraction;

[TestFixture]
public class FoamLogReaderTests
{
    private const string CONVERGED_LOG =
        "Create mesh for time = 0\n\nnCells: 12225\n\nStarting time loop\n\n" +
        "Time = 1\n\nsmoothSolver:  Solving for Ux, Initial residual = 1, Final residual = 0.0538, No Iterations 1\n" +
        "smoothSolver:  Solving for Uy, Initial residual = 1, Final residual = 0.0304, No Iterations 2\n" +
        "GAMG:  Solving for p, Initial residual = 1, Final residual = 0.068, No Iterations 20\n" +
        "--> FOAM Warning : \n    From void something()\n    a warning\n\n" +
        "Time = 2\n\nsmoothSolver:  Solving for Ux, Initial residual = 0.437, Final residual = 0.0234, No Iterations 4\n" +
        "smoothSolver:  Solving for Uy, Initial residual = 0.293, Final residual = 0.0139, No Iterations 4\n" +
        "GAMG:  Solving for p, Initial residual = 0.0234, Final residual = 0.0013, No Iterations 8\n" +
        "\nSIMPLE solution converged in 2 iterations\n\nEnd\n\n";

    private const string DIVERGED_LOG =
        "Time = 1\nGAMG:  Solving for p, Initial residual = 1, Final residual = 0.5, No Iterations 1000\n" +
        "Time = 2\nGAMG:  Solving for p, Initial residual = 3e+40, Final residual = 1e+39, No Iterations 1000\n" +
        "#0  Foam::error::printStack(Foam::Ostream&) at ??:?\n#1  Foam::sigFpe::sigHandler(int) at ??:?\n" +
        "Floating point exception (core dumped)\n";

    #region Reading Tests

    [Test]
    public void AConvergedSteadyRunIsReadCompletelyTest()
    {
        var facts = FoamLogReader.Read(new StringReader(CONVERGED_LOG));

        Assert.That(facts.Iterations, Is.EqualTo(2));
        Assert.That(facts.FinalTime, Is.EqualTo(2));
        Assert.That(facts.Converged, Is.True);
        Assert.That(facts.ReachedEnd, Is.True);
        Assert.That(facts.Fatal, Is.False);
        Assert.That(facts.FloatingPointException, Is.False);
        Assert.That(facts.WarningCount, Is.EqualTo(1));
        Assert.That(facts.CellCount, Is.EqualTo(12225));
        Assert.That(facts.FinalResiduals.Select(residual => residual.Key), Is.EqualTo(new[] { "Ux", "Uy", "p" }));
        Assert.That(facts.FinalResiduals[2].Value, Is.EqualTo(0.0234).Within(1e-12));
    }

    [Test]
    public void ADivergedRunIsSeenAsAFloatingPointExceptionTest()
    {
        var facts = FoamLogReader.Read(new StringReader(DIVERGED_LOG));

        Assert.That(facts.Iterations, Is.EqualTo(2));
        Assert.That(facts.Converged, Is.False);
        Assert.That(facts.ReachedEnd, Is.False);
        Assert.That(facts.FloatingPointException, Is.True);
        Assert.That(facts.FinalResiduals[0].Value, Is.EqualTo(3e40));
    }

    [Test]
    public void AMissingLogYieldsEmptyFactsTest()
    {
        var facts = FoamLogReader.Read(Path.Combine(Path.GetTempPath(), $"no-such-log-{Guid.NewGuid():N}"));

        Assert.That(facts.Iterations, Is.EqualTo(0));
        Assert.That(facts.FinalResiduals, Is.Empty);
        Assert.That(facts.ReachedEnd, Is.False);
    }

    [Test]
    public void AFatalErrorIsFlaggedTest()
    {
        var facts = FoamLogReader.Read(new StringReader("Time = 1\n--> FOAM FATAL IO ERROR: (openfoam-2606)\nkeyword nu is undefined\n"));

        Assert.That(facts.Fatal, Is.True);
        Assert.That(facts.Converged, Is.False);
    }

    #endregion
}
