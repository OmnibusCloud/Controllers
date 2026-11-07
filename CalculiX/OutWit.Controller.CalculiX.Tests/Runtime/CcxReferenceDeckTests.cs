using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.CalculiX.Runtime;

namespace OutWit.Controller.CalculiX.Tests.Runtime;

/// <summary>
/// The generated benchmark deck: the right counts, the boundary faces, the same physics as the
/// ref20 deck that used to be embedded, and the same text on every run (a rate is comparable
/// only against the same deck).
/// </summary>
[TestFixture]
public sealed class CcxReferenceDeckTests
{
    [Test]
    public void TheBenchmarkDeckHasTheDeclaredCountsTest()
    {
        var deck = CcxReferenceDeck.Benchmark();
        var lines = deck.Split('\n');

        Assert.That(CcxReferenceDeck.NODES, Is.EqualTo(64000));
        Assert.That(CcxReferenceDeck.ELEMENTS, Is.EqualTo(59319));
        Assert.That(CountBetween(lines, "*NODE,", "*ELEMENT"), Is.EqualTo(CcxReferenceDeck.NODES));
        Assert.That(CountBetween(lines, "*ELEMENT", "*NSET"), Is.EqualTo(CcxReferenceDeck.ELEMENTS));
        Assert.That(CountBetween(lines, "*CLOAD", "*NODE FILE"), Is.EqualTo(CcxReferenceDeck.SIZE * CcxReferenceDeck.SIZE), "one load per node of the x = 1 face");
    }

    [Test]
    public void ASizeTwentyDeckMatchesTheEmbeddedReferenceStructureTest()
    {
        var lines = CcxReferenceDeck.Generate(20).Split('\n');

        Assert.That(CountBetween(lines, "*NODE,", "*ELEMENT"), Is.EqualTo(8000));
        Assert.That(CountBetween(lines, "*ELEMENT", "*NSET"), Is.EqualTo(6859));
        Assert.That(lines, Does.Contain("1, 1, 2, 22, 21, 401, 402, 422, 421"), "the first brick, numbered as in the embedded deck");
        Assert.That(lines, Does.Contain("1, 7981, 20"), "the fixed face as a GENERATE range, as in the embedded deck");
        Assert.That(lines, Does.Contain("20, 3, -1."), "the first loaded node of the x = 1 face");
        Assert.That(lines, Does.Contain("210000., 0.3"));
    }

    [Test]
    public void TheDeckIsTheSameOnEveryRunTest()
    {
        var first = Hash(CcxReferenceDeck.Benchmark());
        var second = Hash(CcxReferenceDeck.Benchmark());

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void ASizeBelowTwoIsRefusedTest()
    {
        Assert.That(() => CcxReferenceDeck.Generate(1), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private static int CountBetween(string[] lines, string startKeyword, string endKeyword)
    {
        var start = Array.FindIndex(lines, line => line.StartsWith(startKeyword, StringComparison.Ordinal));
        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith(endKeyword, StringComparison.Ordinal));
        return lines.Skip(start + 1).Take(end - start - 1).Count(line => line.Length > 0 && !line.StartsWith('*'));
    }

    private static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
