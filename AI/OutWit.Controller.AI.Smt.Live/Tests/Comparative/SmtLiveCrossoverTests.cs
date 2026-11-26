using OutWit.Controller.AI.Smt.Live.Comparative;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Live.Tests.Comparative;

[TestFixture]
public sealed class SmtLiveCrossoverTests
{
    #region Constants

    private const double TOLERANCE = 1e-9;

    #endregion

    #region Crossing Tests

    [Test]
    public void TheFleetIsFirstAboveTheSizeWhereTheTwoLinesCrossTest()
    {
        // Local: 20 s of start-up, then 1 s a problem. Fleet: (1, 39.8 s) and (3, 40 s): 0.1 s a problem, 39.7 s fixed.
        var crossover = SmtLiveCrossover.Compute(3, 23, 20, 3, 40, (1, 39.8));

        Assert.Multiple(() =>
        {
            Assert.That(crossover.Kind, Is.EqualTo(SmtLiveCrossoverKind.FleetAbove));
            Assert.That(crossover.LocalFixed, Is.EqualTo(20).Within(TOLERANCE));
            Assert.That(crossover.LocalPerProblem, Is.EqualTo(1).Within(TOLERANCE));
            Assert.That(crossover.FleetPerProblem, Is.EqualTo(0.1).Within(TOLERANCE));
            Assert.That(crossover.FleetFixed, Is.EqualTo(39.7).Within(TOLERANCE));
            Assert.That(crossover.Problems, Is.EqualTo(19.7 / 0.9).Within(1e-6), "20 + n = 39.7 + 0.1 n");
        });
    }

    [Test]
    public void AFleetSlowerAProblemThanThisMachineNeverCatchesUpTest()
    {
        // Local: 10 s fixed, 0.1 s a problem. Fleet: 30 s fixed, 0.2 s a problem.
        var crossover = SmtLiveCrossover.Compute(100, 20, 10, 100, 50, (0, 30));

        Assert.That(crossover.Kind, Is.EqualTo(SmtLiveCrossoverKind.LocalAlways));
        Assert.That(crossover.Problems, Is.Null);
    }

    [Test]
    public void ACrossoverAboveTheLargestSetIsNoSizeARunCanHaveTest()
    {
        // Local: 1 s fixed, 1 s a problem. Fleet: 2,000,001 s fixed, 0.999 s a problem: equal at two billion problems.
        var crossover = SmtLiveCrossover.Compute(1000, 1001, 1, 1000, 2_000_001 + 999, (1, 2_000_001 + 0.999));

        Assert.Multiple(() =>
        {
            Assert.That(crossover.Kind, Is.EqualTo(SmtLiveCrossoverKind.LocalAlways));
            Assert.That(crossover.Problems, Is.GreaterThan(SmtProblemSetLimits.MAX_ROWS));
        });
    }

    [Test]
    public void AFleetWhoseFixedCostIsBelowTheStartUpHereIsFirstAtEverySizeWhenItIsAlsoCheaperAProblemTest()
    {
        // Local: 30 s fixed, 1 s a problem. Fleet: 5 s fixed, 0.5 s a problem.
        var crossover = SmtLiveCrossover.Compute(10, 40, 30, 10, 10, (2, 6));

        Assert.That(crossover.Kind, Is.EqualTo(SmtLiveCrossoverKind.FleetAlways));
    }

    [Test]
    public void AFleetWithALowerFixedCostButADearerProblemIsFirstOnlyBelowTheCrossingTest()
    {
        // Local: 30 s fixed, 0.5 s a problem. Fleet: 5 s fixed, 1 s a problem: equal at 50.
        var crossover = SmtLiveCrossover.Compute(100, 80, 30, 100, 105, (10, 15));

        Assert.Multiple(() =>
        {
            Assert.That(crossover.Kind, Is.EqualTo(SmtLiveCrossoverKind.FleetBelow));
            Assert.That(crossover.Problems, Is.EqualTo(50).Within(1e-6));
        });
    }

    [Test]
    public void AFleetFirstOnlyBelowASizeNoSetCanReachIsFirstAtEverySizeASetMayHaveTest()
    {
        // Local: 2,000,030 s fixed, 1 s a problem. Fleet: 5 s fixed, 2 s a problem: equal at two million problems.
        var crossover = SmtLiveCrossover.Compute(100, 2_000_030 + 100, 2_000_030, 100, 205, (10, 25));

        Assert.Multiple(() =>
        {
            Assert.That(crossover.Kind, Is.EqualTo(SmtLiveCrossoverKind.FleetAlways));
            Assert.That(crossover.Problems, Is.GreaterThan(SmtProblemSetLimits.MAX_ROWS));
        });
    }

    #endregion

    #region Not Computed Tests

    [Test]
    public void WithoutASmallFleetRunNoFixedCostIsReadAndNothingIsGuessedTest()
    {
        var crossover = SmtLiveCrossover.Compute(3, 23, 20, 3, 40, null);

        Assert.Multiple(() =>
        {
            Assert.That(crossover.Kind, Is.EqualTo(SmtLiveCrossoverKind.NotComputed));
            Assert.That(crossover.Reason, Does.Contain("no fixed cost of a fleet run was measured").And.Contain("--fixed"));
            Assert.That(crossover.FleetFixed, Is.Null);
            Assert.That(crossover.FleetPerProblem, Is.Null);
            Assert.That(crossover.LocalPerProblem, Is.EqualTo(1).Within(TOLERANCE), "this machine's line is still measured");
        });
    }

    [TestCase(3, 39.0, "not fewer than")]
    [TestCase(1, 41.0, "does not rise")]
    [TestCase(2, 1.0, "below zero")]
    public void ASmallRunThatGivesNoLineIsSaidTest(double smallProblems, double smallWall, string reason)
    {
        // The compared fleet run: 3 problems in 40 s.
        var crossover = SmtLiveCrossover.Compute(3, 23, 20, 3, 40, (smallProblems, smallWall));

        Assert.That(crossover.Kind, Is.EqualTo(SmtLiveCrossoverKind.NotComputed));
        Assert.That(crossover.Reason, Does.Contain(reason));
    }

    [Test]
    public void ALocalRunThatSolvedNothingAfterItsStartUpGivesNoLineTest()
    {
        var crossover = SmtLiveCrossover.Compute(0, 20, 20, 3, 40, (1, 39.8));

        Assert.That(crossover.Kind, Is.EqualTo(SmtLiveCrossoverKind.NotComputed));
        Assert.That(crossover.Reason, Does.Contain("solved no problem"));
    }

    #endregion
}
