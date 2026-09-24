using OutWit.Controller.OpenFOAM.Extraction;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Tests.Utils;

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

    private const string PROBES_P =
        "# Probe 0 (0.1 0.2 0)\n# Probe 1 (0.3 0.2 0)\n#       Probe             0             1\n#        Time\n" +
        "        0.1     -1.23e+00     -0.5e+00\n        0.2     -1.20e+00     -0.48e+00\n";

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
    }

    [Test]
    public void TheExtractorNamesValuesByResponseAndColumnAndSkipsTimeTest()
    {
        var root = OpenFOAMTestPaths.CreateScratch("foam-post");
        try
        {
            var coeffs = Path.Combine(root, "postProcessing", "coeffs");
            Directory.CreateDirectory(Path.Combine(coeffs, "0"));
            Directory.CreateDirectory(Path.Combine(coeffs, "200"));
            File.WriteAllText(Path.Combine(coeffs, "0", "coefficient.dat"), "# Time Cd\n0 9\n");
            File.WriteAllText(Path.Combine(coeffs, "200", "coefficient.dat"), COEFFICIENT_DAT);

            var probes = Path.Combine(root, "postProcessing", "probes", "0");
            Directory.CreateDirectory(probes);
            File.WriteAllText(Path.Combine(probes, "p"), PROBES_P);
            File.WriteAllText(Path.Combine(probes, "U"), "# Probe 0 (0.1 0.2 0)\n#  Time  0\n0.2  (1 2 3)\n");

            var request = new FoamExtractionRequestData
            {
                Responses =
                [
                    new FoamResponseSpecData { Name = "coeffs", Kind = FoamResponseKind.ForceCoeffs },
                    new FoamResponseSpecData { Name = "probes", Kind = FoamResponseKind.Probe, Fields = ["p", "U"] },
                    new FoamResponseSpecData { Name = "absent", Kind = FoamResponseKind.FieldMinMax }
                ]
            };

            var row = FoamResponseExtractor.Extract(root, request);

            var names = row.Values.Select(value => value.Name).ToList();
            Assert.That(names, Does.Contain("coeffs.Cd").And.Contain("coeffs.CmPitch"));
            Assert.That(names, Does.Not.Contain("coeffs.Time"));
            Assert.That(row.Values.First(value => value.Name == "coeffs.Cd").Value, Is.EqualTo(0.418), "the LATEST time directory is read");
            Assert.That(names, Does.Contain("probes.p.c1").And.Contain("probes.U.c3"), "two files under one response carry the file name");
            Assert.That(names.Count(name => name.StartsWith("absent", StringComparison.Ordinal)), Is.EqualTo(0));
        }
        finally
        {
            OpenFOAMTestPaths.TryDelete(root);
        }
    }

    [Test]
    public void ANullRequestYieldsAnEmptyRowTest()
    {
        Assert.That(FoamResponseExtractor.Extract(Path.GetTempPath(), null).Values, Is.Empty);
    }

    #endregion
}
