using OutWit.Controller.CalculiX.Extraction;

namespace OutWit.Controller.CalculiX.Tests.Extraction;

/// <summary>
/// The .dat reader on decks the golden fixtures of the first release did
/// not cover: ccx prints one "forces (fx,fy,fz) for set …" block PER
/// INCREMENT/STEP and the response must be the final state (the audit
/// caught the blocks accumulating into duplicate response names carrying
/// partial-load values); every step is read on its own; the eigenvalue table
/// comes in four layouts and only one of them had the frequency in the
/// fourth column; a row printed in a local *TRANSFORM frame says so.
/// </summary>
[TestFixture]
public class DatResultReaderTests
{
    private string m_testDir = null!;

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"calculix-dat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(m_testDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    #region Tools

    private string WriteDat(string content)
    {
        var path = Path.Combine(m_testDir, "job.dat");
        File.WriteAllText(path, content);
        return path;
    }

    private static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    }

    private static DatForceBlock SingleForces(DatResultFile result)
    {
        return result.Steps.Single().ForceBlocks.Single();
    }

    private static double[] RowSum(DatForceBlock block)
    {
        return [block.Rows.Sum(row => row.Fx), block.Rows.Sum(row => row.Fy), block.Rows.Sum(row => row.Fz)];
    }

    #endregion

    #region Block End Tests

    [Test]
    public void TheEigenvalueTableEndsBeforeTheParticipationFactorsTest()
    {
        // ccx 2.22 prints PARTICIPATION FACTORS, EFFECTIVE MODAL MASS and
        // TOTAL EFFECTIVE MASS after the eigenvalues — rows of the same shape
        // (a leading integer, four or more numbers) that were read as modes
        // 5 to 13 of a 4-mode deck.
        var result = DatResultReader.Read(Fixture("freq.dat"));

        Assert.That(result.Steps.Single().Eigenfrequencies.Select(mode => mode.Cycles),
            Is.EqualTo(new[] { 2.229443e5, 2.229443e5, 4.113257e5, 6.997396e5 }).Within(1e0));
        Assert.That(result.Steps.Single().Eigenfrequencies.Select(mode => mode.Mode), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(result.Steps.Single().Eigenfrequencies.Select(mode => mode.NodalDiameter), Is.All.Null);
    }

    [Test]
    public void BucklingFactorsAreReadOnePerModeTest()
    {
        // buckle.inp, solved by ccx 2.22: *BUCKLE asking for 3 factors.
        var result = DatResultReader.Read(Fixture("buckle.dat"));

        Assert.That(result.Steps.Single().BucklingFactors, Is.EqualTo(new[] { 151.9163, 151.9163, 699.4240 }).Within(1e-3));
        Assert.That(result.Steps.Single().ForceBlocks, Is.Empty);
    }

    [Test]
    public void ABucklingStepKeepsTheBaseStateReactionAndDropsTheModePrintsTest()
    {
        // buckle-prints.inp, solved by ccx 2.22: *BUCKLE with *NODE PRINT RF.
        // The reactions printed before the factors are the base state's (the
        // applied load) and stay; the three blocks printed per mode after
        // them are mode shapes, and the last of them was the sum before.
        var step = DatResultReader.Read(Fixture("buckle-prints.dat")).Steps.Single();

        Assert.That(step.BucklingFactors, Has.Count.EqualTo(3));
        Assert.That(step.ForceBlocks.Single().Time, Is.EqualTo(0.0));
        Assert.That(RowSum(step.ForceBlocks.Single())[0], Is.EqualTo(900.0).Within(1e-3));
        Assert.That(step.ModeShapeForceBlocks, Is.EqualTo(3));
    }

    [Test]
    public void AForcesBlockEndsAtTheNextPrintTest()
    {
        // static-prints.inp, solved by ccx 2.22: *NODE PRINT with RF, U and
        // an *EL PRINT of S. The displacement and stress rows follow the
        // forces rows and have their shape; they were summed into the
        // reaction forces.
        var result = DatResultReader.Read(Fixture("static-prints.dat"));

        var block = SingleForces(result);
        Assert.That(block.SetName, Is.EqualTo("FIX"));
        Assert.That(RowSum(block), Is.EqualTo(new[] { 0.0, 0.0, 100.0 }).Within(1e-6));
        Assert.That(block.Rows.Select(row => row.Local), Is.All.False);
        Assert.That(block.Total, Is.Null);
    }

    #endregion

    #region Force Sum Tests

    [Test]
    public void MultiIncrementForcesKeepTheLastBlockPerSetTest()
    {
        // Two increments of the same step: the 40 % cutback block first,
        // the full-load block second — exactly what a nonlinear static deck
        // prints with the default *NODE PRINT frequency.
        var path = WriteDat(
            " forces (fx,fy,fz) for set FIX and time  0.4000000E+00\n" +
            "\n" +
            "         1  4.000000E+01  0.000000E+00  1.000000E+01\n" +
            "         4  4.000000E+01  0.000000E+00  1.000000E+01\n" +
            "\n" +
            " forces (fx,fy,fz) for set FIX and time  0.1000000E+01\n" +
            "\n" +
            "         1  1.000000E+02  0.000000E+00  2.500000E+01\n" +
            "         4  1.000000E+02  0.000000E+00  2.500000E+01\n");

        var result = DatResultReader.Read(path);

        var block = SingleForces(result);
        Assert.That(block.SetName, Is.EqualTo("FIX"));
        Assert.That(block.Time, Is.EqualTo(1.0));
        Assert.That(RowSum(block), Is.EqualTo(new[] { 200.0, 0.0, 50.0 }).Within(1e-9),
            "the FINAL block's values, not an accumulation across increments");
    }

    [Test]
    public void DistinctSetsKeepDistinctSumsTest()
    {
        var path = WriteDat(
            " forces (fx,fy,fz) for set FIX and time  0.1000000E+01\n" +
            "\n" +
            "         1  1.000000E+02  0.000000E+00  2.500000E+01\n" +
            "\n" +
            " forces (fx,fy,fz) for set LOAD and time  0.1000000E+01\n" +
            "\n" +
            "         9 -1.000000E+02  0.000000E+00 -2.500000E+01\n");

        var result = DatResultReader.Read(path);

        Assert.That(result.Steps.Single().ForceBlocks.Select(block => block.SetName), Is.EquivalentTo(new[] { "FIX", "LOAD" }));
    }

    [Test]
    public void ARowInALocalFrameKeepsItsMarkTest()
    {
        // transform-cyl.dat: *NODE PRINT prints in the node's *TRANSFORM
        // frame by default and marks every such row with a trailing L.
        var result = DatResultReader.Read(Fixture("transform-cyl.dat"));

        var block = SingleForces(result);
        Assert.That(block.Rows, Has.Count.EqualTo(9));
        Assert.That(block.Rows.Select(row => row.Local), Is.All.True);
        Assert.That(block.Rows.Select(row => row.Node), Is.EqualTo(new[] { 1, 12, 23, 34, 45, 56, 67, 78, 89 }));
        Assert.That(RowSum(block), Is.EqualTo(new[] { 1262.115, -2524.23, 0.0 }).Within(1e-2), "summed as printed, the audit's wrong number");
    }

    [Test]
    public void TheGlobalTotalOfTotalsYesIsReadTest()
    {
        // static-totals.dat: the same deck as static.dat with TOTALS=YES.
        // ccx sums the global components itself and prints them after the rows.
        var result = DatResultReader.Read(Fixture("static-totals.dat"));

        var block = SingleForces(result);
        Assert.That(block.Rows, Has.Count.EqualTo(4));
        Assert.That(block.Total, Is.EqualTo(new[] { 0.0, 0.0, 100.0 }).Within(1e-9));
    }

    [Test]
    public void TotalsOnlyPrintsTheTotalAloneTest()
    {
        var path = WriteDat(
            " total force (fx,fy,fz) for set FIX and time  0.1000000E+01\n" +
            "\n" +
            "       -7.105427E-14 -3.907985E-14  1.000000E+02\n");

        var block = SingleForces(DatResultReader.Read(path));

        Assert.That(block.Rows, Is.Empty);
        Assert.That(block.Total, Is.EqualTo(new[] { -7.105427e-14, -3.907985e-14, 100.0 }).Within(1e-9));
    }

    #endregion

    #region Step Tests

    [Test]
    public void EveryStepIsReadOnItsOwnTest()
    {
        // static-freq.dat: a static step with a printed reaction, then a
        // frequency step whose forces are printed per mode. The per-mode
        // blocks replaced the static sum before (rf_sum 0 for a static 900).
        var result = DatResultReader.Read(Fixture("static-freq.dat"));

        Assert.That(result.Steps.Select(step => step.Number), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(RowSum(result.Steps[0].ForceBlocks.Single()), Is.EqualTo(new[] { 0.0, 0.0, 900.0 }).Within(1e-6));
        Assert.That(result.Steps[0].Eigenfrequencies, Is.Empty);
        Assert.That(result.Steps[1].ForceBlocks, Is.Empty, "the forces printed per mode are mode shapes");
        Assert.That(result.Steps[1].ModeShapeForceBlocks, Is.EqualTo(5));
        Assert.That(result.Steps[1].Eigenfrequencies.Select(mode => mode.Cycles), Is.EqualTo(new[] { 1000.459, 1000.459, 6085.65, 6085.65, 8027.393 }).Within(1e-3));
    }

    [Test]
    public void FrequencyStepsNumberTheirModesFromOneTest()
    {
        // freq-freq.dat: 5 modes, a static perturbation, then 3 modes — the
        // second table's modes were numbered 6, 7, 8 before.
        var result = DatResultReader.Read(Fixture("freq-freq.dat"));

        Assert.That(result.Steps.Select(step => step.Number), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(result.Steps[0].Eigenfrequencies.Select(mode => mode.Mode), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(result.Steps[2].Eigenfrequencies.Select(mode => mode.Mode), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(result.Steps[2].Eigenfrequencies[0].Cycles, Is.EqualTo(1281.772).Within(1e-3));
    }

    #endregion

    #region Section Print Tests

    [Test]
    public void SectionPrintsAreReadPerSurfaceTest()
    {
        // The S8R plate with one print per element edge (52); WITEDGE4 is the
        // root edge of element 1 (face S6): half the tip load of 10 and half
        // the root moment of 1000, through a cut of 10 x 2.
        var result = DatResultReader.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "shell-s8r-2d-edges.dat"));

        var step = result.Steps.Single();
        Assert.That(step.Sections, Has.Count.EqualTo(52));
        Assert.That(step.ForceBlocks.Single().SetName, Is.EqualTo("FIX"), "the reactions beside them are read as before");

        var root = step.Sections["witedge4"];
        Assert.That(root.Time, Is.EqualTo(1.0));
        Assert.That(root.Force.Z, Is.EqualTo(-4.9998).Within(1e-4));
        Assert.That((root.Centre.X, root.Centre.Y), Is.EqualTo((0.0, 5.0)));
        Assert.That((root.Normal.X, root.Normal.Y, root.Normal.Z), Is.EqualTo((-1.0, 0.0, 0.0)), "the outward normal of the element's face");
        Assert.That(root.Moment.Y, Is.EqualTo(499.9967).Within(1e-4));
        Assert.That(root.Area, Is.EqualTo(20.0));
    }

    #endregion

    #region Eigenvalue Layout Tests

    [Test]
    public void ACyclicSymmetryTableCarriesTheNodalDiameterTest()
    {
        // cyclic-freq.dat: a 30-degree sector of a ring, nodal diameters 0 to
        // 2, three modes each. The frequency is the fifth number of a row;
        // read as the fourth, the column held the real part in rad/time.
        var result = DatResultReader.Read(Fixture("cyclic-freq.dat"));

        var modes = result.Steps.Single().Eigenfrequencies;
        Assert.That(modes, Has.Count.EqualTo(9));
        Assert.That(modes.Select(mode => mode.NodalDiameter), Is.EqualTo(new int?[] { 0, 0, 0, 1, 1, 1, 2, 2, 2 }));
        Assert.That(modes.Select(mode => mode.Mode), Is.EqualTo(new[] { 1, 2, 3, 1, 2, 3, 1, 2, 3 }));
        Assert.That(modes[6].Cycles, Is.EqualTo(1563.217).Within(1e-3));
        Assert.That(modes[8].Cycles, Is.EqualTo(5265.133).Within(1e-3));
        Assert.That(result.Steps.Single().UnknownEigenvalueTables, Is.EqualTo(0));
    }

    [Test]
    public void AComplexFrequencyTableHasNoEigenvalueColumnTest()
    {
        // complex-freq.dat: *COMPLEX FREQUENCY, CORIOLIS after a *FREQUENCY
        // step. The row is mode, real part, cycles, imaginary part; read as
        // the fourth number, the column held the imaginary part.
        var result = DatResultReader.Read(Fixture("complex-freq.dat"));

        Assert.That(result.Steps.Select(step => step.Number), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(result.Steps[1].Eigenfrequencies.Select(mode => mode.Cycles),
            Is.EqualTo(new[] { 841.8615, 1158.962, 5930.293, 6240.909, 8025.816 }).Within(1e-3));
        Assert.That(result.Steps[1].Eigenfrequencies.Select(mode => mode.NodalDiameter), Is.All.Null);
    }

    [Test]
    public void AnUnknownTableLayoutYieldsNoFrequencyTest()
    {
        var path = WriteDat(
            "     E I G E N V A L U E   O U T P U T\n" +
            "\n" +
            " SOMETHING ELSE ENTIRELY\n" +
            "\n" +
            "      1   0.3951470E+08   0.6286072E+04   0.1000459E+04   0.0000000E+00\n");

        var step = DatResultReader.Read(path).Steps.Single();

        Assert.That(step.Eigenfrequencies, Is.Empty, "never a number from a column that may be the wrong one");
        Assert.That(step.UnknownEigenvalueTables, Is.EqualTo(1));
    }

    #endregion
}
