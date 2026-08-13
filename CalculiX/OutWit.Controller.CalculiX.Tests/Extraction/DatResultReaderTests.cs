using OutWit.Controller.CalculiX.Extraction;

namespace OutWit.Controller.CalculiX.Tests.Extraction;

/// <summary>
/// The .dat reader's force-sum semantics on decks the golden fixtures do not
/// cover: ccx prints one "forces (fx,fy,fz) for set …" block PER
/// INCREMENT/STEP, and the response must be the final state — the audit
/// caught the blocks accumulating into duplicate response names carrying
/// partial-load values.
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

        Assert.That(result.ForceSums, Has.Count.EqualTo(1),
            "one sum per SET, never one per increment");
        Assert.That(result.ForceSums[0].SetName, Is.EqualTo("FIX"));
        Assert.That(result.ForceSums[0].Fx, Is.EqualTo(200.0).Within(1e-9),
            "the FINAL block's values, not an accumulation across increments");
        Assert.That(result.ForceSums[0].Fz, Is.EqualTo(50.0).Within(1e-9));
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

        Assert.That(result.ForceSums.Select(sum => sum.SetName),
            Is.EquivalentTo(new[] { "FIX", "LOAD" }));
    }

    #endregion
}
