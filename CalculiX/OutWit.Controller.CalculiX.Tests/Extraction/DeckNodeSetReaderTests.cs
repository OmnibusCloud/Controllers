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

    [Test]
    public void SetReferencesExpandToAnyDepthTest()
    {
        // Standard ccx/Abaqus: a *NSET data line may list previously
        // defined SET NAMES among the ids — the audit caught them being
        // dropped silently, leaving probes over a subset.
        var path = WriteDeck(
            "*NSET, NSET=BASE\n" +
            "1, 2, 3\n" +
            "*NSET, NSET=CLAMP\n" +
            "BASE, 4\n" +
            "*NSET, NSET=ALLOF\n" +
            "CLAMP, 5\n");

        var sets = DeckNodeSetReader.Read(path);

        Assert.That(sets["CLAMP"], Is.EquivalentTo(new[] { 1, 2, 3, 4 }));
        Assert.That(sets["ALLOF"], Is.EquivalentTo(new[] { 1, 2, 3, 4, 5 }),
            "references chain through referencing sets");
    }

    [Test]
    public void AnUnresolvableReferenceDropsTheWholeSetTest()
    {
        // Loud-missing beats silent-wrong: a probe over a PARTIAL set would
        // return a plausible wrong number, so the set must vanish whole —
        // an absent set skips its probes by the reader's own doctrine.
        var path = WriteDeck(
            "*NSET, NSET=GOOD\n" +
            "1, 2\n" +
            "*NSET, NSET=BROKEN\n" +
            "NOPE, 7\n" +
            "*NSET, NSET=DOWNSTREAM\n" +
            "BROKEN, 8\n" +
            "*NSET, NSET=LOOPA\n" +
            "LOOPB\n" +
            "*NSET, NSET=LOOPB\n" +
            "LOOPA\n");

        var sets = DeckNodeSetReader.Read(path);

        Assert.That(sets.ContainsKey("GOOD"), Is.True);
        Assert.That(sets.ContainsKey("BROKEN"), Is.False, "an unknown reference is unresolvable");
        Assert.That(sets.ContainsKey("DOWNSTREAM"), Is.False, "and the drop is transitive");
        Assert.That(sets.ContainsKey("LOOPA"), Is.False, "a cycle never resolves");
        Assert.That(sets.ContainsKey("LOOPB"), Is.False);
    }

    #endregion
}
