using OutWit.Controller.Simulation.Schwarz.Utils;

namespace OutWit.Controller.Simulation.Schwarz.Tests.Utils;

using Math = System.Math; // must be inside the namespace: OutWit.Math.* shadows System.Math
using OutWit.Math.Simulation.Model.Schwarz;

/// <summary>
/// Whole-job progress of a Schwarz solve read from the round state: it must move steadily through
/// the round loop — the part of a solve the engine's stage counter cannot see into — never go
/// backwards, and stay below 1 until the field is assembled.
/// </summary>
[TestFixture]
public class SchwarzProgressTests
{
    #region Constants

    // A 64³ steady deck on four subdomains, measured on a live pool: the residual contracted by
    // about this factor every round and converged to 1e-8 relative in 125 rounds.
    private const double MEASURED_CONTRACTION = 0.867;
    private const double MEASURED_INITIAL_RESIDUAL = 4245.004;
    private const double EPS = 1e-8;

    #endregion

    #region Loop Tests

    [Test]
    public void NothingIsDoneBeforeTheFirstResidualTest()
    {
        var state = new SchwarzRoundData { Round = 0, Eps = EPS };

        Assert.That(SchwarzProgress.AfterRound(state), Is.EqualTo(0.0));
        Assert.That(SchwarzProgress.ConvergedFraction(state), Is.EqualTo(0.0));
    }

    [Test]
    public void ProgressTracksTheRoundsOfAContractingSolveTest()
    {
        var states = Solve(MEASURED_INITIAL_RESIDUAL, MEASURED_CONTRACTION, EPS);
        var total = states[^1].Round;

        var previous = 0.0;
        foreach (var state in states)
        {
            var progress = SchwarzProgress.AfterRound(state);

            Assert.That(progress, Is.GreaterThanOrEqualTo(previous), $"round {state.Round} went backwards");
            Assert.That(progress, Is.LessThan(1.0));

            // The share of the rounds and the final pass done — a constant contraction makes the
            // logarithmic estimate exact up to the rounding of the last round.
            var expected = (double)state.Round / (total + 1);
            Assert.That(progress, Is.EqualTo(expected).Within(0.02), $"round {state.Round}");

            previous = progress;
        }

        Assert.That(SchwarzProgress.IsConverged(states[^1]), Is.True);
        Assert.That(SchwarzProgress.AfterRound(states[^1]), Is.EqualTo((double)total / (total + 1)));
    }

    [Test]
    public void FinalPassContinuesFromTheLastRoundTest()
    {
        var states = Solve(MEASURED_INITIAL_RESIDUAL, MEASURED_CONTRACTION, EPS);
        var last = states[^1];

        Assert.That(SchwarzProgress.BeforeFinalPass(last), Is.GreaterThanOrEqualTo(SchwarzProgress.AfterRound(last)));
        Assert.That(SchwarzProgress.BeforeFinalPass(last), Is.LessThan(1.0));
    }

    [Test]
    public void ARoundThatBumpsTheResidualDoesNotGoBackwardsTest()
    {
        var before = State(round: 3, residual: 1e-3, history: [1.0, 1e-2, 1e-3]);
        var bumped = State(round: 4, residual: 5e-3, history: [1.0, 1e-2, 1e-3, 5e-3]);

        Assert.That(SchwarzProgress.AfterRound(bumped), Is.GreaterThanOrEqualTo(SchwarzProgress.AfterRound(before)));
    }

    [Test]
    public void BudgetEndedLoopStillLeavesRoomForTheFinalPassTest()
    {
        // A stalled solve runs out of rounds far from its target.
        var stalled = State(round: 60, residual: 0.5, history: [.. Enumerable.Repeat(0.5, 59).Prepend(1.0)]);

        Assert.That(SchwarzProgress.IsConverged(stalled), Is.False);
        Assert.That(SchwarzProgress.AfterRound(stalled), Is.LessThan(0.1));
        Assert.That(SchwarzProgress.BeforeFinalPass(stalled), Is.EqualTo(60.0 / 61.0));
    }

    #endregion

    #region Degenerate State Tests

    [Test]
    public void ZeroResidualIsConvergedTest()
    {
        var state = new SchwarzRoundData { Round = 1, Residual = 0.0, InitialResidual = 0.0, Eps = EPS, History = [0.0] };

        Assert.That(SchwarzProgress.ConvergedFraction(state), Is.EqualTo(1.0));
        Assert.That(SchwarzProgress.AfterRound(state), Is.EqualTo(0.5));
    }

    [Test]
    public void OutOfRangeEpsIsNotANumberTest()
    {
        var loose = State(round: 2, residual: 0.5, history: [1.0, 0.5], eps: 2.0);
        var zero = State(round: 2, residual: 0.5, history: [1.0, 0.5], eps: 0.0);

        Assert.That(SchwarzProgress.ConvergedFraction(loose), Is.EqualTo(1.0));
        Assert.That(SchwarzProgress.ConvergedFraction(zero), Is.EqualTo(0.0));
        Assert.That(double.IsNaN(SchwarzProgress.AfterRound(zero)), Is.False);
    }

    #endregion

    #region Stage Tests

    [Test]
    public void StagesNameTheRoundAndTheTargetTest()
    {
        var plan = new SchwarzPlanData { Parts = 4 };
        var running = State(round: 12, residual: 3.1e-3, history: [4245.004, 3.1e-3]);
        var converged = State(round: 125, residual: 4.08e-5, history: [4245.004, 4.08e-5], eps: 1e-8);

        Assert.That(SchwarzProgress.DescribeFirstRound(plan), Is.EqualTo("round 1: factorizing 4 subdomains"));
        Assert.That(SchwarzProgress.DescribeRound(running), Is.EqualTo("round 12: residual 3.10E-03, target 4.25E-05"));
        Assert.That(SchwarzProgress.DescribeRound(converged), Is.EqualTo("round 125: converged, residual 4.08E-05"));
        Assert.That(SchwarzProgress.DescribeFinalPass(plan), Is.EqualTo("final pass: collecting the field from 4 subdomains"));
    }

    #endregion

    #region Tools

    private static List<SchwarzRoundData> Solve(double initialResidual, double contraction, double eps)
    {
        var states = new List<SchwarzRoundData>();
        var history = new List<double>();
        var residual = initialResidual;

        while (true)
        {
            history.Add(residual);
            var state = new SchwarzRoundData
            {
                Round = history.Count,
                Residual = residual,
                InitialResidual = initialResidual,
                Eps = eps,
                History = [.. history]
            };
            states.Add(state);

            if (SchwarzProgress.IsConverged(state))
                return states;

            residual *= contraction;
        }
    }

    private static SchwarzRoundData State(int round, double residual, List<double> history, double eps = 1e-8)
    {
        return new SchwarzRoundData
        {
            Round = round,
            Residual = residual,
            InitialResidual = history[0],
            Eps = eps,
            History = history
        };
    }

    #endregion
}
