using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// The commands the controller puts around a problem: each one s-expression, written by the
/// writer that writes a problem.
/// </summary>
[TestFixture]
public class SmtScriptsTests
{
    #region Query Tests

    [Test]
    public void CheckSatAndGetModelAreWrittenBareTest()
    {
        Assert.That(SmtScripts.CheckSat().ToString(), Is.EqualTo("(check-sat)"));
        Assert.That(SmtScripts.GetModel().ToString(), Is.EqualTo("(get-model)"));
    }

    [Test]
    public void GetValueListsItsTermsTest()
    {
        Assert.That(SmtScripts.GetValue([Term("x")]).ToString(), Is.EqualTo("(get-value (x))"));
        Assert.That(SmtScripts.GetValue([Term("x"), Term("(f y 1)"), Term("|a b|")]).ToString(), Is.EqualTo("(get-value (x (f y 1) |a b|))"));
    }

    [Test]
    public void GetValueRefusesNoTermsTest()
    {
        Assert.Throws<ArgumentException>(() => SmtScripts.GetValue([]));
        Assert.Throws<ArgumentNullException>(() => SmtScripts.GetValue(null!));
        Assert.Throws<ArgumentException>(() => SmtScripts.GetValue([Term("x"), null!]));
    }

    [Test]
    public void GetValueKeepsItsOwnListTest()
    {
        var terms = new List<SmtExpression> { Term("x"), Term("y") };

        var command = SmtScripts.GetValue(terms);
        terms.Clear();

        Assert.That(command.ToString(), Is.EqualTo("(get-value (x y))"));
    }

    [Test]
    public void GetInfoTakesAKeywordTest()
    {
        Assert.That(SmtScripts.GetInfo(":reason-unknown").ToString(), Is.EqualTo("(get-info :reason-unknown)"));
        Assert.That(SmtScripts.GetInfo(":all-statistics").Items[1].Kind, Is.EqualTo(SmtExpressionKind.Keyword));
    }

    #endregion

    #region Option Tests

    [Test]
    public void SetOptionWritesTheKeywordAndTheValueTest()
    {
        Assert.That(SmtScripts.SetOption(":produce-models", SmtExpression.Symbol("true")).ToString(), Is.EqualTo("(set-option :produce-models true)"));
        Assert.That(SmtScripts.SetOption(":random-seed", SmtExpression.Atom(SmtExpressionKind.Numeral, "7")).ToString(), Is.EqualTo("(set-option :random-seed 7)"));
        Assert.Throws<ArgumentNullException>(() => SmtScripts.SetOption(":produce-models", null!));
    }

    [Test]
    public void TextThatIsNoKeywordIsRefusedTest()
    {
        // A keyword is written into the command as it is given: it is a colon and a simple
        // symbol, or it is not written.
        foreach (var keyword in new[] { "", ":", "rlimit", ":1a", ":a b", ":a) (exit", ":a\n", ":a\0", "::a", ":a\"", ":a|b", ":a;b", " :a", ":a ", ":caf\u00e9" })
        {
            Assert.Throws<ArgumentException>(() => SmtScripts.GetInfo(keyword), keyword);
            Assert.Throws<ArgumentException>(() => SmtScripts.SetOption(keyword, SmtExpression.Symbol("true")), keyword);
        }
    }

    [Test]
    public void MissingKeywordIsRefusedTest()
    {
        Assert.Throws<ArgumentNullException>(() => SmtScripts.GetInfo(null!));
        Assert.Throws<ArgumentNullException>(() => SmtScripts.SetOption(null!, SmtExpression.Symbol("true")));
    }

    [Test]
    public void SetResourceLimitWritesTheLimitTest()
    {
        Assert.That(SmtScripts.SetResourceLimit(0).ToString(), Is.EqualTo("(set-option :rlimit 0)"));
        Assert.That(SmtScripts.SetResourceLimit(50_000_000).ToString(), Is.EqualTo("(set-option :rlimit 50000000)"));
        Assert.That(SmtScripts.SetResourceLimit(4_294_967_295).ToString(), Is.EqualTo("(set-option :rlimit 4294967295)"));
        Assert.That(SmtScripts.SetResourceLimit(1).Items[2].Kind, Is.EqualTo(SmtExpressionKind.Numeral));
    }

    [Test]
    public void SetResourceLimitRefusesWhatTheSolverDoesNotTakeTest()
    {
        // The solver's parameter is 32 bits wide: given more it reports an error and then runs
        // the check with no limit at all, so the value is never written.
        Assert.That(SmtScripts.MAX_RESOURCE_LIMIT, Is.EqualTo(4_294_967_295UL));
        Assert.Throws<ArgumentOutOfRangeException>(() => SmtScripts.SetResourceLimit(4_294_967_296));
        Assert.Throws<ArgumentOutOfRangeException>(() => SmtScripts.SetResourceLimit(ulong.MaxValue));
    }

    #endregion

    #region Assertion Tests

    [Test]
    public void AssertNotNegatesTheGoalTest()
    {
        Assert.That(SmtScripts.AssertNot(Term("(> (f x) 0)")).ToString(), Is.EqualTo("(assert (not (> (f x) 0)))"));
        Assert.That(SmtScripts.AssertNot(Term("p")).ToString(), Is.EqualTo("(assert (not p))"));
        Assert.Throws<ArgumentNullException>(() => SmtScripts.AssertNot(null!));
    }

    [Test]
    public void BlockRulesOutTheValuesOfAllTermsTogetherTest()
    {
        var command = SmtScripts.Block([Term("a"), Term("(select m 0)"), Term("|c d|")], [Term("1"), Term("#x0F"), Term("(- 5)")]);

        Assert.That(command.ToString(), Is.EqualTo("(assert (not (and (= a 1) (= (select m 0) #x0F) (= |c d| (- 5)))))"));
    }

    [Test]
    public void BlockOfOneTermHasNoConjunctionTest()
    {
        Assert.That(SmtScripts.Block([Term("a")], [Term("true")]).ToString(), Is.EqualTo("(assert (not (= a true)))"));
    }

    [Test]
    public void BlockRefusesNoPairsAndUnequalCountsTest()
    {
        Assert.Throws<ArgumentException>(() => SmtScripts.Block([], []));
        Assert.Throws<ArgumentException>(() => SmtScripts.Block([Term("a"), Term("b")], [Term("1")]));
        Assert.Throws<ArgumentException>(() => SmtScripts.Block([Term("a")], [Term("1"), Term("2")]));
        Assert.Throws<ArgumentException>(() => SmtScripts.Block([Term("a")], []));
        Assert.Throws<ArgumentException>(() => SmtScripts.Block([Term("a"), null!], [Term("1"), Term("2")]));
        Assert.Throws<ArgumentNullException>(() => SmtScripts.Block(null!, [Term("1")]));
        Assert.Throws<ArgumentNullException>(() => SmtScripts.Block([Term("a")], null!));
    }

    #endregion

    #region Text Tests

    [Test]
    public void TextWritesOneCommandALineTest()
    {
        var text = SmtScripts.Text([SmtScripts.SetOption(":produce-models", SmtExpression.Symbol("true")), SmtScripts.SetResourceLimit(1_000), SmtScripts.CheckSat(), SmtScripts.GetModel()]);

        Assert.That(text, Is.EqualTo("(set-option :produce-models true)\n(set-option :rlimit 1000)\n(check-sat)\n(get-model)\n"));
        Assert.That(SmtScripts.Text([]), Is.Empty);
        Assert.Throws<ArgumentNullException>(() => SmtScripts.Text(null!));
        Assert.Throws<ArgumentNullException>(() => SmtScripts.Text([SmtScripts.CheckSat(), null!]));
    }

    [Test]
    public void EveryBuiltCommandReadsBackAsItselfTest()
    {
        // Built from s-expressions and written by the one writer: what a term holds cannot end
        // the command it is in, or start another.
        var hostile = new[]
        {
            SmtExpression.Atom(SmtExpressionKind.String, "\")) (exit) (echo \"x"),
            SmtExpression.QuotedSymbol(")) (exit) (set-option :a 1"),
            SmtExpression.QuotedSymbol("check-sat"),
            SmtExpression.Symbol("needs bars"),
            SmtExpression.Atom(SmtExpressionKind.String, "line\nbreak ; not a comment"),
            Term("(f (g \"a\"\"b\") |x y|)")
        };

        var commands = new List<SmtExpression> { SmtScripts.CheckSat(), SmtScripts.GetModel(), SmtScripts.GetInfo(":reason-unknown"), SmtScripts.SetResourceLimit(42), SmtScripts.GetValue(hostile), SmtScripts.Block(hostile, hostile) };
        foreach (var term in hostile)
        {
            commands.Add(SmtScripts.AssertNot(term));
            commands.Add(SmtScripts.GetValue([term]));
            commands.Add(SmtScripts.Block([term], [term]));
            commands.Add(SmtScripts.SetOption(":option", term));
        }

        var read = SmtReader.Read(SmtScripts.Text(commands), SmtReaderLimits.Problem);

        Assert.That(read, Has.Count.EqualTo(commands.Count));
        for (var index = 0; index < commands.Count; index++)
            Assert.That(read[index].Is(commands[index]), Is.True, commands[index].ToString());
    }

    #endregion

    #region Tools

    private static SmtExpression Term(string text)
    {
        return SmtReader.Read(text, SmtReaderLimits.Problem).Single();
    }

    #endregion
}
