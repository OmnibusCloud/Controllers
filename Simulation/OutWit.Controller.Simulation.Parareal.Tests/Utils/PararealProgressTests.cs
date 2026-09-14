using OutWit.Controller.Simulation.Parareal.Utils;

namespace OutWit.Controller.Simulation.Parareal.Tests.Utils;

using OutWit.Math.Simulation.Model.Parareal;

/// <summary>
/// Whole-job progress of a parareal solve read from the plan and the iteration state: it must move
/// wave by wave through the iteration loop — the part of a solve the engine's stage counter cannot
/// see into — never go backwards, and stay below 1 until the timeline is collected.
/// </summary>
[TestFixture]
public class PararealProgressTests
{
    #region Loop Tests

    [Test]
    public void MeasuredSolveReadsAsItsElapsedTimeTest()
    {
        // A 32³ transient deck, 4000 steps on 4 slabs, measured on a live pool: the loop took five
        // iterations (the fifth changed nothing) and the waves ended at 26%, 39%, 52%, 64% and 76%
        // of the time spent after the script's setup, the snapshot wave taking the rest.
        var plan = new PararealPlanData { Slabs = 4 };
        double[] corrections = [6.835673, 0.3113423, 0.01636863, 9.344579e-4, 0.0];
        double[] expected = [0.25, 0.375, 0.5, 0.625, 0.75];

        for (var iteration = 1; iteration <= corrections.Length; iteration++)
        {
            var state = State(iteration, corrections[..iteration], scale: 300.0, eps: 1e-6);
            Assert.That(PararealProgress.AfterIteration(plan, state), Is.EqualTo(expected[iteration - 1]).Within(1e-12), $"iteration {iteration}");
        }

        var last = State(5, corrections, scale: 300.0, eps: 1e-6);
        Assert.That(PararealProgress.IsConverged(last), Is.True);
        Assert.That(PararealProgress.BeforeSnapshotPass(last), Is.EqualTo(0.75).Within(1e-12));
    }

    [Test]
    public void NothingIsDoneBeforeTheFirstCorrectionTest()
    {
        var plan = new PararealPlanData { Slabs = 4 };
        var state = new PararealStateData { Round = 0, Scale = 300.0, Eps = 1e-6 };

        Assert.That(PararealProgress.AfterIteration(plan, state), Is.EqualTo(0.0));
        Assert.That(PararealProgress.BeforeSnapshotPass(state), Is.EqualTo(0.0));
    }

    [Test]
    public void EarlyConvergenceMovesStraightToTheSnapshotWaveTest()
    {
        var plan = new PararealPlanData { Slabs = 8 };
        double[] corrections = [1.0, 0.05, 1e-7];

        var previous = 0.0;
        for (var iteration = 1; iteration <= corrections.Length; iteration++)
        {
            var progress = PararealProgress.AfterIteration(plan, State(iteration, corrections[..iteration], scale: 1.0, eps: 1e-6));
            Assert.That(progress, Is.GreaterThanOrEqualTo(previous), $"iteration {iteration} went backwards");
            previous = progress;
        }

        // Three iterations (2 + 1 + 1) of three plus the snapshot wave (2).
        Assert.That(previous, Is.EqualTo(4.0 / 6.0).Within(1e-12));
    }

    [Test]
    public void ProjectionRevisedUpwardsDoesNotGoBackwardsTest()
    {
        var plan = new PararealPlanData { Slabs = 8 };

        // After two corrections the ratio promises convergence at iteration 5; the third correction
        // contracts far less, and the estimate falls back to the Slabs + 1 ceiling.
        var second = PararealProgress.AfterIteration(plan, State(2, [1.0, 2e-2], scale: 1.0, eps: 1e-6));
        var third = PararealProgress.AfterIteration(plan, State(3, [1.0, 2e-2, 1e-2], scale: 1.0, eps: 1e-6));

        Assert.That(second, Is.EqualTo(3.0 / 8.0).Within(1e-12));
        Assert.That(third, Is.GreaterThanOrEqualTo(second));
    }

    [Test]
    public void ExactTargetWaitsForTheExactnessBoundTest()
    {
        // With a zero target only the exact-slab bound ends the loop: Slabs + 1 iterations.
        var plan = new PararealPlanData { Slabs = 4 };

        var first = PararealProgress.AfterIteration(plan, State(1, [5.0], scale: 1.0, eps: 0.0));
        var last = PararealProgress.AfterIteration(plan, State(5, [5.0, 1.0, 0.2, 0.01, 0.0], scale: 1.0, eps: 0.0));

        Assert.That(first, Is.EqualTo(2.0 / 8.0).Within(1e-12));
        Assert.That(last, Is.EqualTo(6.0 / 8.0).Within(1e-12));
    }

    [Test]
    public void BudgetEndedLoopStillLeavesRoomForTheSnapshotWaveTest()
    {
        var plan = new PararealPlanData { Slabs = 16 };
        var state = State(3, [1.0, 0.9, 0.8], scale: 1.0, eps: 1e-9);

        Assert.That(PararealProgress.IsConverged(state), Is.False);
        Assert.That(PararealProgress.BeforeSnapshotPass(state), Is.GreaterThanOrEqualTo(PararealProgress.AfterIteration(plan, state)));
        Assert.That(PararealProgress.BeforeSnapshotPass(state), Is.EqualTo(4.0 / 6.0).Within(1e-12));
    }

    #endregion

    #region Stage Tests

    [Test]
    public void StagesNameTheIterationAndTheTargetTest()
    {
        var plan = new PararealPlanData { Slabs = 4 };

        Assert.That(PararealProgress.DescribeFirstIteration(plan), Is.EqualTo("iteration 1: propagating 4 time slabs"));
        Assert.That(PararealProgress.DescribeIteration(State(2, [6.835673, 0.3113423], scale: 300.0, eps: 1e-6)),
            Is.EqualTo("iteration 2: correction 3.11E-01, target 3.00E-04"));
        Assert.That(PararealProgress.DescribeIteration(State(5, [6.8, 0.31, 0.016, 9.3e-4, 0.0], scale: 300.0, eps: 1e-6)),
            Is.EqualTo("iteration 5: converged, correction 0.00E+00"));
        Assert.That(PararealProgress.DescribeSnapshotPass(plan), Is.EqualTo("snapshot pass: recomputing 4 time slabs"));
    }

    #endregion

    #region Tools

    private static PararealStateData State(int round, double[] history, double scale, double eps)
    {
        return new PararealStateData
        {
            Round = round,
            CorrectionNorm = history[^1],
            Scale = scale,
            Eps = eps,
            History = [.. history]
        };
    }

    #endregion
}
