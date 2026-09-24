using OutWit.Controller.OpenFOAM.Extraction;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Extraction;

[TestFixture]
public class FoamResponseExtractorTests
{
    private const string COEFFICIENT_DAT =
        "# Force coefficients\n# dragDir   : (1 0 0)\n# liftDir   : (0 0 1)\n" +
        "# Time        \tCd            \tCd(f)         \tCd(r)         \tCl            \tCl(f)         \tCl(r)         \tCmPitch\n" +
        "100\t0.44\t0.30\t0.14\t0.10\t0.06\t0.04\t0.01\n" +
        "200\t0.418\t0.281\t0.137\t0.092\t0.055\t0.037\t0.009\n";

    private const string PROBES_P =
        "# Probe 0 (0.1 0.2 0)\n# Probe 1 (0.3 0.2 0)\n#       Probe             0             1\n#        Time\n" +
        "        0.1     -1.23e+00     -0.5e+00\n        0.2     -1.20e+00     -0.48e+00\n";

    private string m_case = null!;

    [SetUp]
    public void Setup()
    {
        m_case = OpenFOAMTestPaths.CreateScratch("foam-post");
    }

    [TearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_case);
    }

    #region Tools

    private void Write(string relative, string text)
    {
        var path = Path.Combine(m_case, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    #endregion

    #region Extraction Tests

    [Test]
    public void ValuesAreNamedByResponseAndColumnAndTheTimeColumnIsSkippedTest()
    {
        Write("postProcessing/coeffs/0/coefficient.dat", "# Time Cd\n0 9\n");
        Write("postProcessing/coeffs/200/coefficient.dat", COEFFICIENT_DAT);
        Write("postProcessing/probes/0/p", PROBES_P);
        Write("postProcessing/probes/0/U", "# Probe 0 (0.1 0.2 0)\n#  Time  0\n0.2  (1 2 3)\n");

        var request = new FoamExtractionRequestData
        {
            Responses =
            [
                new FoamResponseSpecData { Name = "coeffs", Kind = FoamResponseKind.ForceCoeffs },
                new FoamResponseSpecData { Name = "probes", Kind = FoamResponseKind.Probe, Fields = ["p", "U"] },
                new FoamResponseSpecData { Name = "absent", Kind = FoamResponseKind.FieldMinMax }
            ]
        };

        var row = FoamResponseExtractor.Extract(m_case, request);

        var names = row.Values.Select(value => value.Name).ToList();
        Assert.That(names, Does.Contain("coeffs.Cd").And.Contain("coeffs.CmPitch"));
        Assert.That(names, Does.Not.Contain("coeffs.Time"));
        Assert.That(row.Values.First(value => value.Name == "coeffs.Cd").Value, Is.EqualTo(0.418), "the LATEST time directory is read");
        Assert.That(names, Does.Contain("probes.p.c1").And.Contain("probes.U.c3"), "two files under one response carry the file name");
        Assert.That(names.Count(name => name.StartsWith("absent", StringComparison.Ordinal)), Is.EqualTo(0), "a response whose function object wrote nothing contributes nothing");
    }

    [Test]
    public void AMultiFieldMinMaxRowIsReadWholeTest()
    {
        // v2606 writes every field of a fieldMinMax in one row of one file.
        Write("postProcessing/pRange/0/fieldMinMax.dat", "# Time min(p) max(p) min(U) max(U)\n281 -8.4 12.1 0 10.2\n");

        var row = FoamResponseExtractor.Extract(m_case, new FoamExtractionRequestData
        {
            Responses = [new FoamResponseSpecData { Name = "pRange", Kind = FoamResponseKind.FieldMinMax, Fields = ["p", "U"] }]
        });

        Assert.That(row.Values.Select(value => value.Name), Is.EqualTo(new[] { "pRange.min(p)", "pRange.max(p)", "pRange.min(U)", "pRange.max(U)" }));
        Assert.That(row.Values[1].Value, Is.EqualTo(12.1));
    }

    [Test]
    public void ANullRequestYieldsAnEmptyRowTest()
    {
        Assert.That(FoamResponseExtractor.Extract(m_case, null).Values, Is.Empty);
        Assert.That(FoamResponseExtractor.Extract(m_case, new FoamExtractionRequestData()).Values, Is.Empty);
    }

    [Test]
    public void ACaseWithoutPostProcessingYieldsAnEmptyRowTest()
    {
        var row = FoamResponseExtractor.Extract(m_case, new FoamExtractionRequestData
        {
            Responses = [new FoamResponseSpecData { Name = "coeffs", Kind = FoamResponseKind.ForceCoeffs }]
        });

        Assert.That(row.Values, Is.Empty);
    }

    #endregion

    #region Time Directory Tests

    [Test]
    public void TheLatestTimeDirectoryIsChosenNumericallyTest()
    {
        foreach (var time in new[] { "0", "100", "1e-05", "250.5", "notATime" })
            Directory.CreateDirectory(Path.Combine(m_case, "postProcessing", "coeffs", time));

        var latest = FoamResponseExtractor.LatestTimeDirectory(Path.Combine(m_case, "postProcessing", "coeffs"));

        Assert.That(Path.GetFileName(latest), Is.EqualTo("250.5"));
        Assert.That(FoamResponseExtractor.LatestTimeDirectory(Path.Combine(m_case, "postProcessing", "nothing")), Is.Null);
    }

    #endregion
}
