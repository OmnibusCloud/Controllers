using OutWit.Controller.CalculiX.Extraction;

namespace OutWit.Controller.CalculiX.Tests.Extraction;

/// <summary>
/// The streaming .frd reader: step, time and mode from the headers, rows
/// handed out as they are read (continuation lines joined), geometry on
/// request, and a value that is not a number reported as a broken row
/// rather than as NaN (what "nan" parsed to) or 0 (what "-nan(ind)" did).
/// </summary>
[TestFixture]
public class FrdResultReaderTests
{
    #region Read Tests

    [Test]
    public void HeadersGiveEveryBlockItsStepTimeAndModeTest()
    {
        var recorder = new Recorder();

        FrdResultReader.Read(
        [
            "    1PSTEP                         1           1           1          ",
            "  100CL  101 1.000000000          12                     0    1           1",
            " -4  DISP        4    1",
            " -5  D1          1    2    1    0",
            " -5  D2          1    2    2    0",
            " -5  D3          1    2    3    0",
            " -5  ALL         1    2    0    0    1ALL",
            " -1         1 1.00000E-03 2.00000E-03 3.00000E-03",
            " -3",
            "    1PSTEP                         2           1           2          ",
            "    1PMODE                         3                                  ",
            "  100CL  102 222944.3471          12                     2    3MODAL      1",
            " -4  DISP        4    1",
            " -5  D1          1    2    1    0",
            " -5  D2          1    2    2    0",
            " -5  D3          1    2    3    0",
            " -1         1 4.00000E-03 5.00000E-03 6.00000E-03",
            " -3"
        ], recorder);

        Assert.That(recorder.Headers.Select(header => (header.Step, header.Mode, header.IsModeShape)),
            Is.EqualTo(new[] { (1, 0, false), (2, 3, true) }));
        Assert.That(recorder.Headers[0].Components, Is.EqualTo(new[] { "D1", "D2", "D3" }), "the calculated ALL is not stored");
        Assert.That(recorder.Headers[0].Time, Is.EqualTo(1.0));
        Assert.That(recorder.Headers[1].Time, Is.EqualTo(222944.3471).Within(1e-4));
        Assert.That(recorder.Rows, Is.EqualTo(new[] { (1, 1.0e-3, 2.0e-3, 3.0e-3), (1, 4.0e-3, 5.0e-3, 6.0e-3) }));
        Assert.That(recorder.Ends, Is.EqualTo(2));
    }

    [Test]
    public void AValueThatIsNotANumberBreaksItsRowOnlyTest()
    {
        var recorder = new Recorder();

        FrdResultReader.Read(
        [
            " -4  DISP        4    1",
            " -5  D1          1    2    1    0",
            " -5  D2          1    2    2    0",
            " -5  D3          1    2    3    0",
            " -1         1 1.00000E-03 2.00000E-03 3.00000E-03",
            " -1         2         nan 2.00000E-03 3.00000E-03",
            " -1         3 1.00000E-03   -nan(ind) 3.00000E-03",
            " -1         4 1.00000E-03 2.00000E-03         inf",
            " -1         5 7.00000E-03 8.00000E-03 9.00000E-03",
            " -3"
        ], recorder);

        Assert.That(recorder.Rows.Select(row => row.Node), Is.EqualTo(new[] { 1, 5 }));
        Assert.That(recorder.Broken, Is.EqualTo(new[] { (2, "nan"), (3, "-nan(ind)"), (4, "inf") }));
    }

    [Test]
    public void ContinuationLinesCompleteARowTest()
    {
        // More than six stored components spill onto a -2 line.
        var recorder = new Recorder();

        FrdResultReader.Read(
        [
            " -4  CONTACT     8    1",
            " -5  COPEN       1    4    1    0",
            " -5  CSLIP1      1    4    2    0",
            " -5  CSLIP2      1    4    3    0",
            " -5  CPRESS      1    4    4    0",
            " -5  CSHEAR1     1    4    5    0",
            " -5  CSHEAR2     1    4    6    0",
            " -5  EXTRA1      1    4    7    0",
            " -1         9 1.00000E+00 2.00000E+00 3.00000E+00 4.00000E+00 5.00000E+00 6.00000E+00",
            " -2           7.00000E+00",
            " -3"
        ], recorder);

        Assert.That(recorder.Headers.Single().Components, Has.Count.EqualTo(7));
        Assert.That(recorder.Wide, Is.EqualTo(new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0 }));
    }

    [Test]
    public void GeometryIsHandedOutOnRequestOnlyTest()
    {
        string[] lines =
        [
            "    2C                             2                                     1",
            " -1       370 0.00000E+00 0.00000E+00-1.00000E+00",
            " -1       371 2.50000E+00 0.00000E+00 1.00000E+00",
            " -3",
            "    3C                             1                                     1",
            " -1         1    1    0    1",
            " -2       370       371",
            " -3"
        ];
        var asked = new Recorder { WantsGeometry = true };
        var unasked = new Recorder();

        FrdResultReader.Read(lines, asked);
        FrdResultReader.Read(lines, unasked);

        Assert.That(asked.Nodes, Is.EqualTo(new[] { (370, 0.0, 0.0, -1.0), (371, 2.5, 0.0, 1.0) }));
        Assert.That(unasked.Nodes, Is.Empty);
        Assert.That(asked.Rows, Is.Empty, "element lines are not rows");
    }

    [Test]
    public void TheInMemoryReadStillCollectsEveryBlockTest()
    {
        var blocks = FrdResultReader.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "static.frd"));

        Assert.That(blocks.Select(block => block.Name), Is.EqualTo(new[] { "DISP", "STRESS", "ERROR" }));
        Assert.That(blocks[0].Values, Has.Count.EqualTo(12));
        Assert.That(blocks[0].Step, Is.EqualTo(1));
    }

    #endregion

    #region Nested Types

    private sealed class Recorder : IFrdResultVisitor
    {
        public List<FrdBlockHeader> Headers { get; } = [];

        public List<(int Node, double X, double Y, double Z)> Rows { get; } = [];

        public double[] Wide { get; private set; } = [];

        public List<(int Node, string Text)> Broken { get; } = [];

        public List<(int Id, double X, double Y, double Z)> Nodes { get; } = [];

        public int Ends { get; private set; }

        public bool WantsGeometry { get; init; }

        public void Node(int id, double x, double y, double z)
        {
            Nodes.Add((id, x, y, z));
        }

        public void BeginBlock(FrdBlockHeader header)
        {
            Headers.Add(header);
        }

        public void Row(int node, double[] values)
        {
            if (values.Length == 3)
                Rows.Add((node, values[0], values[1], values[2]));
            else
                Wide = (double[])values.Clone();
        }

        public void BrokenRow(int node, string text)
        {
            Broken.Add((node, text));
        }

        public void EndBlock()
        {
            Ends++;
        }
    }

    #endregion
}
