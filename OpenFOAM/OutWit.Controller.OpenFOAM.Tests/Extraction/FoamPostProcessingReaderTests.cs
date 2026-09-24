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

    #region Reading Tests

    [Test]
    public void TheLastRowOfACoefficientFileIsReadWithItsColumnNamesTest()
    {
        var table = FoamPostProcessingReader.ReadLastRow(new StringReader(COEFFICIENT_DAT));

        Assert.That(table, Is.Not.Null);
        Assert.That(table!.Value.Columns, Is.EqualTo(new[] { "Time", "Cd", "Cd(f)", "Cd(r)", "Cl", "Cl(f)", "Cl(r)", "CmPitch" }));
        Assert.That(table.Value.Values[1], Is.EqualTo(0.418));
        Assert.That(table.Value.Values[0], Is.EqualTo(200));
    }

    [Test]
    public void VectorColumnsAreFlattenedAndGetPositionalNamesTest()
    {
        var table = FoamPostProcessingReader.ReadLastRow(new StringReader(FORCE_DAT_WITH_VECTORS));

        Assert.That(table, Is.Not.Null);
        Assert.That(table!.Value.Values, Has.Count.EqualTo(19));
        Assert.That(table.Value.Columns[0], Is.EqualTo("Time"));
        Assert.That(table.Value.Columns[1], Is.EqualTo("c1"));
        Assert.That(table.Value.Values[4], Is.EqualTo(0.1));
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
        Assert.That(table!.Value.Columns, Is.EqualTo(new[] { "Time", "c1", "c2" }));
        Assert.That(table.Value.Values, Is.EqualTo(new[] { 1.0, 10.0, 20.0 }));
    }

    #endregion
}
