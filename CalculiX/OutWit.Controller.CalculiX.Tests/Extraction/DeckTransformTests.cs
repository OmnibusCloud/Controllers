using OutWit.Controller.CalculiX.Extraction;

namespace OutWit.Controller.CalculiX.Tests.Extraction;

/// <summary>
/// The local frames of *TRANSFORM, rebuilt as ccx builds them
/// (transformatrix.f), so that a vector ccx printed in a local frame comes
/// back to the global frame exactly.
/// </summary>
[TestFixture]
public class DeckTransformTests
{
    #region Rotation Tests

    [Test]
    public void ARectangularFrameRotatesBackTest()
    {
        // e1 = a = (0,1,0); e2 = b made normal to e1 = (-1,0,0); e3 = e1 x e2 = (0,0,1).
        var transform = new DeckTransform("A", cylindrical: false, [0.0, 1.0, 0.0, -1.0, 0.5, 0.0]);

        var global = transform.ToGlobal([1.0, 2.0, 3.0], [7.0, 7.0, 7.0]);

        Assert.That(global, Is.EqualTo(new[] { -2.0, 1.0, 3.0 }).Within(1e-12));
    }

    [Test]
    public void ACylindricalFrameIsBuiltAtTheNodeTest()
    {
        // Axis along x through (0,5,5): at (0,10,5) the radial e1 is (0,1,0),
        // e3 the axis (1,0,0), e2 = e3 x e1 = (0,0,1).
        var transform = new DeckTransform("FIX", cylindrical: true, [0.0, 5.0, 5.0, 100.0, 5.0, 5.0]);

        var global = transform.ToGlobal([1.0, 2.0, 3.0], [0.0, 10.0, 5.0]);

        Assert.That(global, Is.EqualTo(new[] { 3.0, 1.0, 2.0 }).Within(1e-12));
    }

    [Test]
    public void ANodeOnTheCylinderAxisTakesCcxsFixedPerpendicularTest()
    {
        // transformatrix.f: a point on the axis gets e1 = (-e3y/e3x, 1, 0) when
        // the axis has an x component - the same choice the printed row used.
        var transform = new DeckTransform("FIX", cylindrical: true, [0.0, 5.0, 5.0, 100.0, 5.0, 5.0]);

        var global = transform.ToGlobal([1.0, 2.0, 3.0], [0.0, 5.0, 5.0]);

        Assert.That(global, Is.EqualTo(new[] { 3.0, 1.0, 2.0 }).Within(1e-12));
    }

    [Test]
    public void TheCylinderDeckSumsToTheGlobalReactionTest()
    {
        // transform-cyl.dat: nine FIX nodes printed with the trailing L, every
        // one in a different radial frame; the tip load is 9 x (0, 50, -100).
        // Summed as printed they give 1262 / -2524 / 0 (the audit's number);
        // rotated back they give the equilibrium reaction 0 / -450 / 900.
        var facts = DeckFactsReader.Read(Fixture("transform-cyl.inp"));
        var block = DatResultReader.Read(Fixture("transform-cyl.dat")).Steps.Single().ForceBlocks.Single();
        var coordinates = DeckFactsReader.ReadCoordinates(Fixture("transform-cyl.inp"), block.Rows.Select(row => row.Node).ToHashSet());

        var sum = new double[3];
        foreach (var row in block.Rows)
        {
            Assert.That(row.Local, Is.True);
            var global = facts.TransformOf(row.Node)!.ToGlobal([row.Fx, row.Fy, row.Fz], coordinates[row.Node]);
            for (var axis = 0; axis < 3; axis++)
                sum[axis] += global[axis];
        }

        Assert.That(sum, Is.EqualTo(new[] { 0.0, -450.0, 900.0 }).Within(1e-3));
    }

    #endregion

    #region Tools

    private static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    }

    #endregion
}
