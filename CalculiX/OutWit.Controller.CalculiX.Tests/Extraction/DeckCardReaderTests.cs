using OutWit.Controller.CalculiX.Extraction;

namespace OutWit.Controller.CalculiX.Tests.Extraction;

/// <summary>
/// The card reader reads keyword lines the way ccx does: every blank
/// removed, upper-cased, continued over a trailing comma; comments and blank
/// lines skipped; data lines attached to their card.
/// </summary>
[TestFixture]
public class DeckCardReaderTests
{
    #region Read Tests

    [Test]
    public void KeywordLinesLoseTheirBlanksAndCaseTest()
    {
        var lines = DeckCardReader.Read(["* Node Print , NSET = Fix, Totals = yes", "RF"]).ToList();

        Assert.That(lines, Has.Count.EqualTo(2));
        Assert.That(lines[0].IsKeyword, Is.True);
        Assert.That(lines[0].Card.Keyword, Is.EqualTo("NODEPRINT"));
        Assert.That(lines[0].Card.Value("NSET"), Is.EqualTo("FIX"));
        Assert.That(lines[0].Card.Value("TOTALS"), Is.EqualTo("YES"));
        Assert.That(lines[1].Data, Is.EqualTo("RF"), "data lines are only trimmed");
        Assert.That(lines[1].Card, Is.SameAs(lines[0].Card));
    }

    [Test]
    public void AKeywordLineEndingInACommaContinuesOnTheNextLineTest()
    {
        var lines = DeckCardReader.Read(["*HEAT TRANSFER, STEADY STATE,", "  SOLVER=SPOOLES", "1., 1."]).ToList();

        Assert.That(lines, Has.Count.EqualTo(2));
        Assert.That(lines[0].Card.Keyword, Is.EqualTo("HEATTRANSFER"));
        Assert.That(lines[0].Card.Has("STEADYSTATE"), Is.True);
        Assert.That(lines[0].Card.Value("SOLVER"), Is.EqualTo("SPOOLES"));
        Assert.That(lines[0].Card.LineIndex, Is.EqualTo(0));
        Assert.That(lines[1].Data, Is.EqualTo("1., 1."));
    }

    [Test]
    public void CommentsBlankLinesAndLeadingDataAreSkippedTest()
    {
        var lines = DeckCardReader.Read(["", "orphan data", "** a comment", "*HEADING", "   ", "my deck"]).ToList();

        Assert.That(lines.Select(line => line.Data), Is.EqualTo(new[] { null, "my deck" }));
    }

    [Test]
    public void ARepeatedParameterTakesItsLastValueTest()
    {
        var card = DeckCardReader.Read(["*ELEMENT, TYPE=C3D8, TYPE=S8R"]).Single().Card;

        Assert.That(card.Value("TYPE"), Is.EqualTo("S8R"));
        Assert.That(card.Value("ELSET"), Is.Null);
        Assert.That(card.Has("TYPE"), Is.True);
    }

    [Test]
    public void NormalizeRemovesEveryBlankAndTabTest()
    {
        Assert.That(DeckCardReader.Normalize("*El File ,\tOutput = 2d "), Is.EqualTo("*ELFILE,OUTPUT=2D"));
    }

    #endregion
}
