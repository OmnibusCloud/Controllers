using System.Text;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

[TestFixture]
public class SmtExpressionTests
{
    #region Write Tests

    [Test]
    public void WriteSeparatesItemsByOneSpaceTest()
    {
        var expression = SmtReader.Read("( assert\n\t(=  x   ( +  y 1 ) ) ) ; done", SmtReaderLimits.Problem).Single();

        Assert.That(expression.ToString(), Is.EqualTo("(assert (= x (+ y 1)))"));
    }

    [Test]
    public void WriteKeepsEveryTokenAsItWasWrittenTest()
    {
        const string text = "(f :named 0 12 1.50 #x0F #xab #b0101 () (()))";

        Assert.That(SmtReader.Read(text, SmtReaderLimits.Problem).Single().ToString(), Is.EqualTo(text));
    }

    [Test]
    public void WriteDoublesTheQuotesOfAStringTest()
    {
        Assert.That(SmtExpression.Atom(SmtExpressionKind.String, "say \"hi\"").ToString(), Is.EqualTo("\"say \"\"hi\"\"\""));
        Assert.That(SmtExpression.Atom(SmtExpressionKind.String, "").ToString(), Is.EqualTo("\"\""));
    }

    [Test]
    public void WriteDropsTheBarsASymbolDoesNotNeedTest()
    {
        Assert.That(SmtReader.Read("|abc|", SmtReaderLimits.Problem).Single().ToString(), Is.EqualTo("abc"));
        Assert.That(SmtReader.Read("|x!1|", SmtReaderLimits.Problem).Single().ToString(), Is.EqualTo("x!1"));
    }

    [TestCase("a b")]
    [TestCase("")]
    [TestCase("1abc")]
    [TestCase("a'b")]
    [TestCase("a\nb")]
    [TestCase("caf\u00e9")]
    [TestCase("x:y")]
    [TestCase("(x)")]
    public void WriteKeepsTheBarsASymbolNeedsTest(string name)
    {
        var symbol = SmtReader.Read($"|{name}|", SmtReaderLimits.Problem).Single();

        Assert.That(symbol.ToString(), Is.EqualTo($"|{name}|"));
        Assert.That(SmtExpression.Symbol(name).ToString(), Is.EqualTo($"|{name}|"));
    }

    [Test]
    public void WriteKeepsAReservedWordBetweenItsBarsTest()
    {
        foreach (var word in SmtReservedWords.General.Concat(SmtReservedWords.Commands))
        {
            Assert.That(SmtReader.Read($"|{word}|", SmtReaderLimits.Problem).Single().ToString(), Is.EqualTo($"|{word}|"), word);
            Assert.That(SmtReader.Read(word, SmtReaderLimits.Problem).Single().ToString(), Is.EqualTo(word), word);
        }
    }

    [Test]
    public void WriteTakesNestingFarDeeperThanACallStackTest()
    {
        const int depth = 200_000;
        var text = new string('(', depth) + "x" + new string(')', depth);

        var expression = SmtReader.Read(text, SmtReaderLimits.Unbounded).Single();

        Assert.That(expression.ToString(), Is.EqualTo(text));
        Assert.That(expression.Is(SmtReader.Read(text, SmtReaderLimits.Unbounded).Single()), Is.True);
    }

    [Test]
    public void WriteAppendsToWhatTheBuilderHoldsTest()
    {
        var builder = new StringBuilder("; ");

        SmtExpression.List(SmtExpression.Symbol("check-sat")).WriteTo(builder);

        Assert.That(builder.ToString(), Is.EqualTo("; (check-sat)"));
    }

    #endregion

    #region Factory Tests

    [Test]
    public void FactoriesBuildWhatTheReaderReadsTest()
    {
        var built = SmtExpression.List(
            SmtExpression.Symbol("assert"),
            SmtExpression.List(
                SmtExpression.Symbol("="),
                SmtExpression.QuotedSymbol("a b"),
                SmtExpression.Atom(SmtExpressionKind.Numeral, "7"),
                SmtExpression.Atom(SmtExpressionKind.String, "s")));

        var read = SmtReader.Read("(assert (= |a b| 7 \"s\"))", SmtReaderLimits.Problem).Single();

        Assert.That(built.Is(read), Is.True);
        Assert.That(built.ToString(), Is.EqualTo("(assert (= |a b| 7 \"s\"))"));
        Assert.That(built.Line, Is.EqualTo(0));
    }

    [TestCase("a|b")]
    [TestCase("a\\b")]
    public void FactoriesRefuseANameNoSymbolCanHaveTest(string name)
    {
        Assert.Throws<ArgumentException>(() => SmtExpression.Symbol(name));
        Assert.Throws<ArgumentException>(() => SmtExpression.QuotedSymbol(name));
    }

    [Test]
    public void AtomRefusesTheKindsThatHaveTheirOwnFactoryTest()
    {
        Assert.Throws<ArgumentException>(() => SmtExpression.Atom(SmtExpressionKind.List, ""));
        Assert.Throws<ArgumentException>(() => SmtExpression.Atom(SmtExpressionKind.Symbol, "x"));
    }

    #endregion

    #region Comparison Tests

    [Test]
    public void IsComparesShapeKindAndTextTest()
    {
        var expression = Read("(f x 1 \"s\")");

        Assert.That(expression.Is(Read("( f x\n1 \"s\" )")), Is.True);
        Assert.That(expression.Is(Read("(f x 1 \"t\")")), Is.False);
        Assert.That(expression.Is(Read("(f x 1)")), Is.False);
        Assert.That(expression.Is(Read("(f (x) 1 \"s\")")), Is.False);
        Assert.That(expression.Is(Read("(f x 1 s)")), Is.False);
        Assert.That(expression.Is(null), Is.False);
    }

    [Test]
    public void IsTakesAQuotedSymbolForTheSimpleOneOfItsNameTest()
    {
        Assert.That(Read("|abc|").Is(Read("abc")), Is.True);
        Assert.That(Read("|a b|").Is(Read("|a b|")), Is.True);
    }

    [Test]
    public void IsTellsAReservedWordFromTheSymbolBetweenBarsTest()
    {
        Assert.That(Read("|let|").Is(Read("let")), Is.False);
        Assert.That(Read("|let|").Is(Read("|let|")), Is.True);
    }

    [Test]
    public void IsTellsANumeralFromTheSymbolOfTheSameTextTest()
    {
        Assert.That(Read("|12|").Is(Read("12")), Is.False);
    }

    #endregion

    #region Symbol Tests

    [Test]
    public void IsSymbolAndIsApplicationMatchByNameTest()
    {
        var expression = Read("(|assert| true)");

        Assert.That(expression.IsApplication("assert"), Is.True);
        Assert.That(expression.Head, Is.EqualTo("assert"));
        Assert.That(expression.Items[0].IsSymbol("assert"), Is.True);
        Assert.That(expression.Items[0].IsQuoted, Is.True);
        Assert.That(Read("(\"assert\" true)").Head, Is.Null);
        Assert.That(Read("()").Head, Is.Null);
        Assert.That(Read("x").Head, Is.Null);
    }

    [Test]
    public void ReservedWordsAreTheStandardsTest()
    {
        Assert.That(SmtReservedWords.General, Has.Count.EqualTo(13));
        Assert.That(SmtReservedWords.Commands, Has.Count.EqualTo(30));
        Assert.That(SmtReservedWords.IsGeneral("let"), Is.True);
        Assert.That(SmtReservedWords.IsGeneral("assert"), Is.False);
        Assert.That(SmtReservedWords.IsCommand("check-sat-assuming"), Is.True);
        Assert.That(SmtReservedWords.Contains("par"), Is.True);
        Assert.That(SmtReservedWords.Contains("lambda"), Is.False);
    }

    #endregion

    #region Tools

    private static SmtExpression Read(string text)
    {
        return SmtReader.Read(text, SmtReaderLimits.Problem).Single();
    }

    #endregion
}
