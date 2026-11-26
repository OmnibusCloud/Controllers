using System.Text;
using OutWit.Controller.AI.Smt.Live.Commands;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Live.Tests.Commands;

/// <summary>
/// How the tool describes a problem set before it is sent: its rows are counted as the model
/// package's reader counts them (a line ends at a line feed; a line of spaces, tabs and carriage
/// returns is blank), so the <c>input</c> line and <c>fleet.json</c> say the number the server
/// reads.
/// </summary>
[TestFixture]
public sealed class SmtLiveInputTests
{
    #region Fields

    private string m_root = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = SmtLiveFixtures.NewFolder();
    }

    [TearDown]
    public void TearDown()
    {
        SmtLiveFixtures.Delete(m_root);
    }

    #endregion

    #region Tests

    [TestCase("a\nb\n", 2)]
    [TestCase("a\r\nb", 2)]
    [TestCase("a\n\n \t\r\nb\n", 2)]
    [TestCase("a\rb\n", 1)]
    [TestCase("a\n\u00a0\nb\n", 3)]
    [TestCase("\ufeffa\n", 1)]
    [TestCase("", 0)]
    public async Task TheRowsAreCountedAsTheReaderCountsThemTest(string text, int rows)
    {
        var path = Path.Combine(m_root, "set.jsonl");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));

        var described = await SmtLiveInput.DescribeAsync(path, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(described.Rows, Is.EqualTo(rows));
            Assert.That(described.Rows, Is.EqualTo(ReaderRows(path)), "the reader of the model package agrees");
            Assert.That(described.Bytes, Is.EqualTo(new FileInfo(path).Length));
            Assert.That(described.Sha256, Is.EqualTo(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))));
        });
    }

    #endregion

    #region Tools

    /// <summary>The rows the model package's reader reads: every one it takes or refuses.</summary>
    private static int ReaderRows(string path)
    {
        using var stream = File.OpenRead(path);
        var reader = new SmtProblemSetReader(stream, new SmtOptionsData());
        var rows = 0;
        while (reader.TryRead(out _))
            rows++;

        return rows;
    }

    #endregion
}
