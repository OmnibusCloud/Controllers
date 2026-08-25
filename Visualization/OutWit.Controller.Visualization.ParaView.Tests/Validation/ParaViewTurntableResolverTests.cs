using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tasks;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tests.Validation;

/// <summary>
/// The camera-move math (docs 06, part B): the per-output azimuth / elevation / dolly of the
/// presets a client composes — orbit, rise, spiral, approach, rock — the validation bounds, and the
/// task identity that carries the whole transform plus the orbit axis and time mode (audit C-M3).
/// </summary>
[TestFixture]
public sealed class ParaViewTurntableResolverTests
{
    #region Constants

    private const double TOLERANCE = 1e-9;

    #endregion

    #region Tests

    [Test]
    public void OrbitKeepsTheTurntableSemanticsTest()
    {
        var plan = ParaViewTurntableResolver.Resolve([0], new ParaViewTurntableData { Frames = 4, Degrees = 360.0 });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Select(me => me.AzimuthDegrees), Is.EqualTo(new[] { 0.0, 90.0, 180.0, 270.0 }).Within(TOLERANCE), "the last output stops short of the first: a 360° orbit loops");
            Assert.That(plan.Select(me => me.ElevationDegrees), Is.All.EqualTo(0.0));
            Assert.That(plan.Select(me => me.DollyFactor), Is.All.EqualTo(1.0));
        });
    }

    [Test]
    public void RiseReachesTheFullElevationOnTheLastOutputTest()
    {
        var plan = ParaViewTurntableResolver.Resolve([2], new ParaViewTurntableData { Frames = 5, Degrees = 0.0, ElevationDegrees = 80.0 });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Select(me => me.TimestepIndex), Is.All.EqualTo(2));
            Assert.That(plan.Select(me => me.AzimuthDegrees), Is.All.EqualTo(0.0));
            Assert.That(plan.Select(me => me.ElevationDegrees), Is.EqualTo(new[] { 0.0, 20.0, 40.0, 60.0, 80.0 }).Within(TOLERANCE), "a rise is not cyclic: the last output IS the top");
            Assert.That(plan.Select(me => me.DollyFactor), Is.All.EqualTo(1.0));
        });
    }

    [Test]
    public void SpiralCombinesOrbitAndRiseTest()
    {
        var plan = ParaViewTurntableResolver.Resolve([0], new ParaViewTurntableData { Frames = 3, Degrees = 360.0, ElevationDegrees = 60.0 });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Select(me => me.AzimuthDegrees), Is.EqualTo(new[] { 0.0, 120.0, 240.0 }).Within(TOLERANCE));
            Assert.That(plan.Select(me => me.ElevationDegrees), Is.EqualTo(new[] { 0.0, 30.0, 60.0 }).Within(TOLERANCE));
        });
    }

    [Test]
    public void ApproachScalesTheDistanceGeometricallyTest()
    {
        var plan = ParaViewTurntableResolver.Resolve([0], new ParaViewTurntableData { Frames = 3, Degrees = 0.0, DollyFactor = 0.25 });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Select(me => me.DollyFactor), Is.EqualTo(new[] { 1.0, 0.5, 0.25 }).Within(TOLERANCE), "geometric progression: every output closes the same ratio, the last reaches the factor");
            Assert.That(plan.Select(me => me.AzimuthDegrees), Is.All.EqualTo(0.0));
            Assert.That(plan.Select(me => me.ElevationDegrees), Is.All.EqualTo(0.0));
        });
    }

    [Test]
    public void RockSwaysAroundTheCapturedFramingAndLoopsTest()
    {
        var plan = ParaViewTurntableResolver.Resolve([0], new ParaViewTurntableData { Frames = 4, Degrees = 60.0, Oscillate = true });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Select(me => me.AzimuthDegrees), Is.EqualTo(new[] { 0.0, 30.0, 0.0, -30.0 }).Within(TOLERANCE), "±half the sweep, back through the captured framing: the sequence loops seamlessly");
            Assert.That(plan.Select(me => me.ElevationDegrees), Is.All.EqualTo(0.0));
            Assert.That(plan.Select(me => me.DollyFactor), Is.All.EqualTo(1.0));
        });
    }

    [Test]
    public void OscillatingRiseAndDollySwayAlikeTest()
    {
        var plan = ParaViewTurntableResolver.Resolve([0], new ParaViewTurntableData { Frames = 4, Degrees = 0.0, ElevationDegrees = 40.0, DollyFactor = 2.0, Oscillate = true });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Select(me => me.ElevationDegrees), Is.EqualTo(new[] { 0.0, 20.0, 0.0, -20.0 }).Within(TOLERANCE));
            Assert.That(plan.Select(me => me.DollyFactor), Is.EqualTo(new[] { 1.0, 2.0, 1.0, 0.5 }).Within(TOLERANCE), "the distance breathes between the factor and its inverse");
        });
    }

    [Test]
    public void SingleOutputMoveRendersTheCapturedFramingTest()
    {
        var plan = ParaViewTurntableResolver.Resolve([0], new ParaViewTurntableData { Frames = 1, Degrees = 360.0, ElevationDegrees = 80.0, DollyFactor = 0.5 });

        Assert.That(plan, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(plan[0].AzimuthDegrees, Is.EqualTo(0.0));
            Assert.That(plan[0].ElevationDegrees, Is.EqualTo(0.0), "no second output to reach the top in");
            Assert.That(plan[0].DollyFactor, Is.EqualTo(1.0));
        });
    }

    [Test]
    public void AdvancingMoveSpreadsTheTimestepsAndProgressesTheMoveTest()
    {
        var plan = ParaViewTurntableResolver.Resolve([0, 1, 2, 3, 4], new ParaViewTurntableData { Frames = 3, Degrees = 180.0, ElevationDegrees = 30.0, TimeMode = ParaViewTurntableTimeMode.Advancing });

        Assert.Multiple(() =>
        {
            Assert.That(plan.Select(me => me.TimestepIndex), Is.EqualTo(new[] { 0, 2, 4 }));
            Assert.That(plan.Select(me => me.AzimuthDegrees), Is.EqualTo(new[] { 0.0, 60.0, 120.0 }).Within(TOLERANCE));
            Assert.That(plan.Select(me => me.ElevationDegrees), Is.EqualTo(new[] { 0.0, 15.0, 30.0 }).Within(TOLERANCE));
        });
    }

    [Test]
    public void ValidationAcceptsEveryPresetTest()
    {
        var presets = new Dictionary<string, ParaViewTurntableData>
        {
            ["orbit"] = new() { Frames = 72, Degrees = 360.0 },
            ["rise"] = new() { Frames = 48, Degrees = 0.0, ElevationDegrees = 80.0 },
            ["spiral"] = new() { Frames = 72, Degrees = 360.0, ElevationDegrees = 60.0 },
            ["approach"] = new() { Frames = 48, Degrees = 0.0, DollyFactor = 0.4 },
            ["retreat"] = new() { Frames = 48, Degrees = 0.0, DollyFactor = 3.0 },
            ["rock"] = new() { Frames = 48, Degrees = 40.0, Oscillate = true },
            ["breathe"] = new() { Frames = 48, Degrees = 0.0, DollyFactor = 1.2, Oscillate = true }
        };

        foreach (var (name, preset) in presets)
        {
            var errors = new List<string>();
            ParaViewTurntableResolver.Validate(preset, errors);
            Assert.That(errors, Is.Empty, name);
        }
    }

    [Test]
    public void ValidationRefusesAMoveThatMovesNothingTest()
    {
        var errors = new List<string>();
        ParaViewTurntableResolver.Validate(new ParaViewTurntableData { Frames = 10, Degrees = 0.0 }, errors);

        Assert.That(errors, Has.Some.Contains("moves nothing"));
    }

    [TestCase(180.0)]
    [TestCase(-171.0)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void ValidationBoundsTheElevationTest(double elevation)
    {
        var errors = new List<string>();
        ParaViewTurntableResolver.Validate(new ParaViewTurntableData { Frames = 10, Degrees = 360.0, ElevationDegrees = elevation }, errors);

        Assert.That(errors, Has.Some.Contains("elevation"));
    }

    [TestCase(0.0)]
    [TestCase(0.01)]
    [TestCase(21.0)]
    [TestCase(-1.0)]
    [TestCase(double.NaN)]
    public void ValidationBoundsTheDollyFactorTest(double dolly)
    {
        var errors = new List<string>();
        ParaViewTurntableResolver.Validate(new ParaViewTurntableData { Frames = 10, Degrees = 360.0, DollyFactor = dolly }, errors);

        Assert.That(errors, Has.Some.Contains("dolly"));
    }

    [Test]
    public void ValidationStillBoundsTheSweepAndTheFramesTest()
    {
        var wide = new List<string>();
        ParaViewTurntableResolver.Validate(new ParaViewTurntableData { Frames = 10, Degrees = 7200.0 }, wide);
        var none = new List<string>();
        ParaViewTurntableResolver.Validate(new ParaViewTurntableData { Frames = 0, Degrees = 360.0 }, none);

        Assert.Multiple(() =>
        {
            Assert.That(wide, Has.Some.Contains("exceeds"));
            Assert.That(none, Has.Some.Contains("at least 1"));
        });
    }

    [Test]
    public void TaskIdentityCarriesTheTransformAxisAndTimeModeTest()
    {
        const string package = "0123456789abcdef";
        const string options = "fedcba9876543210";
        var orbit = new ParaViewOrbitStep(0, 1, 90.0);
        var raised = new ParaViewOrbitStep(0, 1, 90.0, 30.0);
        var closer = new ParaViewOrbitStep(0, 1, 90.0, 0.0, 0.5);

        var aboutViewUp = ParaViewPackageDigest.ComputeTaskId(package, "", "RenderView1", 0, options, orbit, ParaViewCameraAxes.VIEW_UP, ParaViewTurntableTimeMode.Fixed);
        var aboutZ = ParaViewPackageDigest.ComputeTaskId(package, "", "RenderView1", 0, options, orbit, ParaViewCameraAxes.Z, ParaViewTurntableTimeMode.Fixed);
        var advancing = ParaViewPackageDigest.ComputeTaskId(package, "", "RenderView1", 0, options, orbit, ParaViewCameraAxes.VIEW_UP, ParaViewTurntableTimeMode.Advancing);
        var withRise = ParaViewPackageDigest.ComputeTaskId(package, "", "RenderView1", 0, options, raised, ParaViewCameraAxes.VIEW_UP, ParaViewTurntableTimeMode.Fixed);
        var withDolly = ParaViewPackageDigest.ComputeTaskId(package, "", "RenderView1", 0, options, closer, ParaViewCameraAxes.VIEW_UP, ParaViewTurntableTimeMode.Fixed);
        var again = ParaViewPackageDigest.ComputeTaskId(package, "", "RenderView1", 0, options, orbit, ParaViewCameraAxes.VIEW_UP, ParaViewTurntableTimeMode.Fixed);
        var plain = ParaViewPackageDigest.ComputeTaskId(package, "", "RenderView1", 0, options);

        Assert.Multiple(() =>
        {
            Assert.That(again, Is.EqualTo(aboutViewUp), "deterministic");
            Assert.That(aboutZ, Is.Not.EqualTo(aboutViewUp), "the same azimuth about another axis is another picture");
            Assert.That(advancing, Is.Not.EqualTo(aboutViewUp), "the time mode is part of the identity");
            Assert.That(withRise, Is.Not.EqualTo(aboutViewUp));
            Assert.That(withDolly, Is.Not.EqualTo(aboutViewUp));
            Assert.That(plain, Is.Not.EqualTo(aboutViewUp), "a move never collides with the plain task of its timestep");
        });
    }

    #endregion
}
