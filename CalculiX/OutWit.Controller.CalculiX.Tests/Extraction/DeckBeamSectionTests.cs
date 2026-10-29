using OutWit.Controller.CalculiX.Extraction;

namespace OutWit.Controller.CalculiX.Tests.Extraction;

/// <summary>
/// The section von Mises from the section forces ccx writes under SECTION
/// FORCES, in its order: shear 1, shear 2, axial force, torque, moment about
/// the 1-direction, moment about the 2-direction. The expected values are
/// beam theory by hand; the measured ones are ccx 2.22's section forces of
/// cantilevers whose analytic stress is known.
/// </summary>
[TestFixture]
public class DeckBeamSectionTests
{
    #region Rectangle Tests

    [Test]
    public void ARectangleBendsAcrossTheDimensionTheMomentTurnsTest()
    {
        // a = 10 along the 1-direction, b = 20 along the 2-direction.
        var section = new DeckBeamSection("RECT", [10.0, 20.0]);

        Assert.That(section.VonMises([0, 0, 0, 0, 1e4, 0]), Is.EqualTo(1e4 / (10.0 * 20.0 * 20.0 / 6.0)).Within(1e-9), "M1 / (a b^2 / 6) = 15");
        Assert.That(section.VonMises([0, 0, 0, 0, 0, 1e4]), Is.EqualTo(1e4 / (20.0 * 10.0 * 10.0 / 6.0)).Within(1e-9), "M2 / (b a^2 / 6) = 30");
        Assert.That(section.VonMises([0, 0, 1000, 0, -1e4, 1e4]), Is.EqualTo(5.0 + 15.0 + 30.0).Within(1e-9), "N/A + M1/W1 + M2/W2 at a corner");
        Assert.That(section.VonMises([0, 0, -1000, 0, 0, 0]), Is.EqualTo(5.0).Within(1e-9), "compression as tension");
    }

    [Test]
    public void ARectangleCarriesTorsionAndShearAtItsMidSidesTest()
    {
        // Square 10 x 10 in torsion: tau = T / (0.208 a^3) at the middle of
        // every side (Timoshenko and Goodier), von Mises sqrt(3) tau.
        var square = new DeckBeamSection("RECT", [10.0, 10.0]);
        Assert.That(square.VonMises([0, 0, 0, 1000, 0, 0]), Is.EqualTo(System.Math.Sqrt(3.0) * 1000 / (0.208 * 1000)).Within(1e-9));

        // 10 x 20 (long side twice the short): tau = T / (0.246 * 20 * 10^2)
        // on the long side - ccx's torque at the root of a twisted cantilever
        // of 1000 is 1010.89 (B32): sqrt(3) * 2.0546 = 3.5588.
        var oblong = new DeckBeamSection("RECT", [10.0, 20.0]);
        Assert.That(oblong.VonMises([0, 0, 0, 1010.89, 0, 0]), Is.EqualTo(3.5588).Within(1e-4));

        // A shear force alone: 1.5 V / A at the centre.
        Assert.That(square.VonMises([0, 100, 0, 0, 0, 0]), Is.EqualTo(System.Math.Sqrt(3.0) * 1.5).Within(1e-9));
    }

    [Test]
    public void TheMeasuredRootForcesOfACantileverGiveItsStressTest()
    {
        // B32 cantilever, RECT 10 x 10, 100 at the tip of 100: the root node
        // of the ccx 2.22 .frd (beam-b32-2d-forces), 60 analytic.
        var section = new DeckBeamSection("RECT", [10.0, 10.0]);

        Assert.That(section.VonMises([3.33288e-10, 79.3779, 3.14913e-10, 1.17667e-09, -10016.8, 3.38086e-08]), Is.EqualTo(60.1008).Within(1e-4));
    }

    #endregion

    #region Tube Tests

    [Test]
    public void APipeBendsAndTwistsAtItsRimTest()
    {
        // Outer radius 5, wall 1: I = pi (5^4 - 4^4) / 4 = 289.81.
        var pipe = new DeckBeamSection("PIPE", [5.0, 1.0]);
        var inertia = System.Math.PI * (625.0 - 256.0) / 4.0;

        Assert.That(pipe.VonMises([0, 0, 0, 0, 6e3, 8e3]), Is.EqualTo(1e4 * 5.0 / inertia).Within(1e-9), "the resultant moment: 172.53");
        Assert.That(pipe.VonMises([0, 0, 0, 1000, 0, 0]), Is.EqualTo(System.Math.Sqrt(3.0) * 1000 * 5.0 / (2.0 * inertia)).Within(1e-9), "T r / J: 8.63");

        // ccx's root moment of a B32R pipe cantilever under 100 at 100: 9984.34.
        Assert.That(pipe.VonMises([99.996, 0, 0, 0, 0, 9984.34]), Is.EqualTo(172.2555).Within(1e-3));
    }

    [Test]
    public void ABoxBendsAtItsCornersAndTwistsAlongItsWallsTest()
    {
        // 10 x 20 outside, walls of 1: I1 = (10 * 20^3 - 8 * 18^3) / 12 = 2778.67.
        var box = new DeckBeamSection("BOX", [10.0, 20.0, 1.0, 1.0, 1.0, 1.0]);

        Assert.That(box.VonMises([0, 0, 0, 0, 1e4, 0]), Is.EqualTo(1e4 * 10.0 / 2778.6667).Within(1e-3));

        // Bredt: tau = T / (2 A_m t), A_m = 9 x 19.
        Assert.That(box.VonMises([0, 0, 0, 1000, 0, 0]), Is.EqualTo(System.Math.Sqrt(3.0) * 1000 / (2.0 * 9.0 * 19.0)).Within(1e-9));
    }

    #endregion

    #region Refusal Tests

    [Test]
    public void SectionsTheForcesCannotServeSayWhyTest()
    {
        Assert.That(new DeckBeamSection("CIRC", [5.0, 5.0]).Unsupported(), Does.StartWith("a CIRC section, whose section forces ccx integrates 8 % high"));
        Assert.That(new DeckBeamSection("BOX", [10.0, 20.0, 1.0, 2.0, 1.0, 1.0]).Unsupported(), Does.Contain("unequal opposite walls"));
        Assert.That(new DeckBeamSection("RECT", [10.0, 20.0]).Unsupported(), Is.Null);
        Assert.That(double.IsNaN(new DeckBeamSection("CIRC", [5.0, 5.0]).VonMises([0, 0, 0, 0, 1, 0])), Is.True);
    }

    #endregion
}
