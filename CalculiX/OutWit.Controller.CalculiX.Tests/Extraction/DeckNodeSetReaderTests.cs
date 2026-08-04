using OutWit.Controller.CalculiX.Extraction;

namespace OutWit.Controller.CalculiX.Tests.Extraction;

/// <summary>
/// The node-set reader against every declaration shape the probe menu can
/// offer: explicit *NSET lists, GENERATE ranges, and the inline
/// <c>*NODE, NSET=…</c> form the load-deck live run caught missing — the
/// client-side classifier offered NALL, the node side silently dropped the
/// probe.
/// </summary>
[TestFixture]
public class DeckNodeSetReaderTests
{
    private string m_testDir = null!;

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"calculix-nset-{Guid.NewGuid():N}");
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

    private string WriteDeck(string content)
    {
        var path = Path.Combine(m_testDir, "deck.inp");
        File.WriteAllText(path, content);
        return path;
    }

    #endregion

    #region Read Tests

    [Test]
    public void ExplicitNsetListIsReadTest()
    {
        var path = WriteDeck(
            "*NSET, NSET=XMAX\n" +
            "6, 12, 18,\n" +
            "24\n");

        var sets = DeckNodeSetReader.Read(path);

        Assert.That(sets["XMAX"], Is.EquivalentTo(new[] { 6, 12, 18, 24 }));
    }

    [Test]
    public void GenerateRangeIsExpandedTest()
    {
        var path = WriteDeck(
            "*NSET, NSET=SPAN, GENERATE\n" +
            "10, 20, 5\n");

        var sets = DeckNodeSetReader.Read(path);

        Assert.That(sets["SPAN"], Is.EquivalentTo(new[] { 10, 15, 20 }));
    }

    [Test]
    public void InlineNodeCardSetIsReadTest()
    {
        // The gate/load decks declare their all-nodes set exactly this way;
        // only the leading token of a *NODE data line is a node id.
        var path = WriteDeck(
            "*NODE, NSET=NALL\n" +
            "1, 0.000000, 0.000000, 0.000000\n" +
            "2, 1, 0, 0\n" +
            "3, 0.500000, 1.000000, 0.000000\n" +
            "*NSET, NSET=XMAX\n" +
            "2, 3\n");

        var sets = DeckNodeSetReader.Read(path);

        Assert.That(sets["NALL"], Is.EquivalentTo(new[] { 1, 2, 3 }),
            "integer-looking coordinates must never join the set");
        Assert.That(sets["nall"], Is.EquivalentTo(new[] { 1, 2, 3 }), "names are case-insensitive");
        Assert.That(sets["XMAX"], Is.EquivalentTo(new[] { 2, 3 }));
    }

    [Test]
    public void NodeCardWithoutNsetContributesNothingTest()
    {
        var path = WriteDeck(
            "*NODE\n" +
            "1, 0.0, 0.0, 0.0\n");

        Assert.That(DeckNodeSetReader.Read(path), Is.Empty);
    }

    #endregion
}
