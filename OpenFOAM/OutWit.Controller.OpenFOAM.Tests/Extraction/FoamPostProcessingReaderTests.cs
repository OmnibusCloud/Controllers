using OutWit.Controller.OpenFOAM.Extraction;

namespace OutWit.Controller.OpenFOAM.Tests.Extraction;

[TestFixture]
public class FoamPostProcessingReaderTests
{
    private const string COEFFICIENT_DAT =
        "# Force coefficients\n# dragDir   : (1 0 0)\n# liftDir   : (0 0 1)\n" +
        "# Time        \tCd            \tCd(f)         \tCd(r)         \tCl            \tCl(f)         \tCl(r)         \tCmPitch\n" +
        "100\t0.44\t0.30\t0.14\t0.10\t0.06\t0.04\t0.01\n" +
        "200\t0.418\t0.281\t0.137\t0.092\t0.055\t0.037\t0.009\n";

    private const string FORCE_DAT_WITH_VECTORS =
        "# Forces\n# Time forces(pressure viscous porous) moment(pressure viscous porous)\n" +
        "100 ((1 2 3) (0.1 0.2 0.3) (0 0 0)) ((4 5 6) (0.4 0.5 0.6) (0 0 0))\n";

    /// <summary>A scalar probes file as v2606 writes it (pitzDaily, 2026-10-28).</summary>
    private const string PROBES_P =
        "# Probe 0 (0.1 0 0)\n# Probe 1 (0.2 0 0)\n# Time        0             1            \n" +
        "281           -3.7          11.1         \n282           -3.68615      11.0716      \n";

    /// <summary>A vector probes file as v2606 writes it: the components of each probe in parentheses.</summary>
    private const string PROBES_U =
        "# Probe 0 (0.1 0 0)\n# Probe 1 (0.2 0 0)\n# Time        0             1            \n" +
        "282           (6.37979 -0.493236 0) (5.85681 -0.512941 0)\n";

    #region Reading Tests

    [Test]
    public void TheLastRowOfACoefficientFileIsReadWithItsColumnNamesTest()
    {
        var table = FoamPostProcessingReader.ReadLastRow(new StringReader(COEFFICIENT_DAT));

        Assert.That(table, Is.Not.Null);
        Assert.That(table!.Columns, Is.EqualTo(new[] { "Time", "Cd", "Cd(f)", "Cd(r)", "Cl", "Cl(f)", "Cl(r)", "CmPitch" }));
        Assert.That(table.Values[1], Is.EqualTo(0.418));
        Assert.That(table.Values[0], Is.EqualTo(200));
        Assert.That(table.Notes, Is.Empty);
        Assert.That(table.IsProbes, Is.False);
    }

    [Test]
    public void VectorColumnsAreFlattenedAndGetPositionalNamesTest()
    {
        var table = FoamPostProcessingReader.ReadLastRow(new StringReader(FORCE_DAT_WITH_VECTORS));

        Assert.That(table, Is.Not.Null);
        Assert.That(table!.Values, Has.Count.EqualTo(19));
        Assert.That(table.Columns[0], Is.EqualTo("Time"));
        Assert.That(table.Columns[1], Is.EqualTo("c1"));
        Assert.That(table.Values[4], Is.EqualTo(0.1));
    }

    [Test]
    public void AFileWithoutDataRowsYieldsNullTest()
    {
        Assert.That(FoamPostProcessingReader.ReadLastRow(new StringReader("# only\n# headers\n")), Is.Null);
        Assert.That(FoamPostProcessingReader.ReadLastRow(new StringReader(string.Empty)), Is.Null);
    }

    [Test]
    public void ARowShorterThanItsHeaderGetsPositionalNamesTest()
    {
        var table = FoamPostProcessingReader.ReadLastRow(new StringReader("# Time a b c\n1 10 20\n"));

        Assert.That(table, Is.Not.Null);
        Assert.That(table!.Columns, Is.EqualTo(new[] { "Time", "c1", "c2" }));
        Assert.That(table.Values, Is.EqualTo(new[] { 1.0, 10.0, 20.0 }));
    }

    #endregion

    #region Probe Tests

    [Test]
    public void AScalarProbeColumnIsNamedByFieldNumberAndPositionTest()
    {
        var table = FoamPostProcessingReader.ReadLastRow(new StringReader(PROBES_P), "p");

        Assert.That(table, Is.Not.Null);
        Assert.That(table!.IsProbes, Is.True);
        Assert.That(table.Columns, Is.EqualTo(new[] { "Time", "p@probe0(0.1 0 0)", "p@probe1(0.2 0 0)" }));
        Assert.That(table.Values, Is.EqualTo(new[] { 282, -3.68615, 11.0716 }));
        Assert.That(table.Notes, Is.Empty);
    }

    [Test]
    public void AVectorProbeColumnCarriesItsComponentTest()
    {
        // Before: the header's three tokens against seven values fell back to
        // c1..c6, which named neither the probe nor the component.
        var table = FoamPostProcessingReader.ReadLastRow(new StringReader(PROBES_U), "U");

        Assert.That(table, Is.Not.Null);
        Assert.That(table!.Columns, Is.EqualTo(new[]
        {
            "Time",
            "U@probe0(0.1 0 0)_x", "U@probe0(0.1 0 0)_y", "U@probe0(0.1 0 0)_z",
            "U@probe1(0.2 0 0)_x", "U@probe1(0.2 0 0)_y", "U@probe1(0.2 0 0)_z"
        }));
        Assert.That(table.Values[4], Is.EqualTo(5.85681));
    }

    [Test]
    public void ATensorProbeColumnCarriesItsComponentTest()
    {
        var symmetric = FoamPostProcessingReader.ReadLastRow(new StringReader("# Probe 0 (1 2 3)\n# Time 0\n1 (1 2 3 4 5 6)\n"), "R");
        var full = FoamPostProcessingReader.ReadLastRow(new StringReader("# Probe 0 (1 2 3)\n# Time 0\n1 (1 2 3 4 5 6 7 8 9)\n"), "gradU");

        Assert.That(symmetric!.Columns.Skip(1), Is.EqualTo(new[] { "R@probe0(1 2 3)_xx", "R@probe0(1 2 3)_xy", "R@probe0(1 2 3)_xz", "R@probe0(1 2 3)_yy", "R@probe0(1 2 3)_yz", "R@probe0(1 2 3)_zz" }));
        Assert.That(full!.Columns.Skip(1).Select(column => column[^2..]), Is.EqualTo(new[] { "xx", "xy", "xz", "yx", "yy", "yz", "zx", "zy", "zz" }));
    }

    [Test]
    public void AProbeOutsideTheMeshIsLeftOutWithANoteTest()
    {
        // v2606 marks it in the header and writes -VGREAT for it (the default
        // includeOutOfBounds): a number that is no measurement.
        var text = "# Probe 0 (0.1 0 0)\n# Probe 1 (5 5 5)  # Not Found\n# Time        0             1            \n" +
                   "282           -3.68615      -1e+300      \n";

        var table = FoamPostProcessingReader.ReadLastRow(new StringReader(text), "p");

        Assert.That(table, Is.Not.Null);
        Assert.That(table!.Columns, Is.EqualTo(new[] { "Time", "p@probe0(0.1 0 0)", "p@probe1(5 5 5)" }));
        Assert.That(table.Values[1], Is.EqualTo(-3.68615));
        Assert.That(double.IsNaN(table.Values[2]), Is.True, "not a value");
        Assert.That(table.Notes, Is.EqualTo(new[] { "probe 1 (5 5 5) of p is outside the mesh - not reported." }));

        var vector = FoamPostProcessingReader.ReadLastRow(new StringReader("# Probe 0 (5 5 5)  # Not Found\n# Time 0\n1 (-1e+300 -1e+300 -1e+300)\n"), "U");
        Assert.That(vector!.Values.Skip(1).All(double.IsNaN), Is.True, "every component of the probe");
        Assert.That(vector.Notes, Has.Count.EqualTo(1));
    }

    [Test]
    public void AProbeTheHeaderDoesNotListIsNotAColumnTest()
    {
        // With includeOutOfBounds off, v2606 lists and writes only the probes
        // it found: the columns follow the header's indices, not the count.
        var text = "# Probe 0 (0.1 0 0)\n# Probe 1 (5 5 5)  # Not Found\n# Probe 2 (0.2 0 0)\n# Time 0 2\n282 -3.68615 11.0716\n";

        var table = FoamPostProcessingReader.ReadLastRow(new StringReader(text), "p");

        Assert.That(table!.Columns, Is.EqualTo(new[] { "Time", "p@probe0(0.1 0 0)", "p@probe2(0.2 0 0)" }));
        Assert.That(table.Notes, Is.Empty);
    }

    [Test]
    public void AProbesFileReadWithoutItsFieldFallsBackToPositionsTest()
    {
        var table = FoamPostProcessingReader.ReadLastRow(new StringReader(PROBES_U));

        Assert.That(table!.IsProbes, Is.False);
        Assert.That(table.Columns, Is.EqualTo(new[] { "Time", "c1", "c2", "c3", "c4", "c5", "c6" }));
    }

    [Test]
    public void AnUnsetValueInAnyFileIsLeftOutTest()
    {
        var table = FoamPostProcessingReader.ReadLastRow(new StringReader("# Time a b\n1 -1e+300 2\n"));

        Assert.That(double.IsNaN(table!.Values[1]), Is.True);
        Assert.That(table.Values[2], Is.EqualTo(2));
        Assert.That(table.Notes, Is.EqualTo(new[] { "a is -1e+300, a probe outside the mesh - not reported." }));
    }

    #endregion
}
