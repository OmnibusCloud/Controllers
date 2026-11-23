using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Solver;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// The reading of what the solver answers to one command: a verdict, an error with its message,
/// the reason of an unknown, the resource counter among the statistics, the values of terms. An
/// answer that is not what its command is answered with is said to be unreadable, and nothing
/// throws for it.
/// </summary>
[TestFixture]
public sealed class SmtAnswersTests
{
    #region Verdict Tests

    [TestCase("sat\n", "sat")]
    [TestCase("unsat\n", "unsat")]
    [TestCase("unknown\n", "unknown")]
    [TestCase("  sat\r\n", "sat")]
    [TestCase("sat", "sat")]
    [TestCase("", "none")]
    [TestCase("SAT\n", "none")]
    [TestCase("satisfiable\n", "none")]
    [TestCase("sat\nsat\n", "none")]
    [TestCase("(error \"line 1 column 1: no\")\n", "none")]
    public void AVerdictIsOneOfThreeWordsAndNothingElseTest(string answer, string verdict)
    {
        Assert.That(SmtSolveWords.Of(SmtAnswers.Verdict(answer)), Is.EqualTo(verdict));
    }

    #endregion

    #region Error Tests

    [TestCase("(error \"line 7 column 11: unknown constant y\")\n", true)]
    [TestCase("\n  (error \"x\")", true)]
    [TestCase("(error : no position\")\n", true)]
    [TestCase("sat\n(error \"later\")\n", false)]
    [TestCase("(\n  (define-fun error () Int\n    1)\n)\n", false)]
    [TestCase("((error 1))\n", false)]
    [TestCase("unsupported\n", false)]
    [TestCase("", false)]
    public void AnErrorIsAnAnswerThatStartsAsOneTest(string answer, bool error)
    {
        Assert.That(SmtAnswers.IsError(answer), Is.EqualTo(error));
    }

    [TestCase("(error \"line 7 column 11: unknown constant y\")\n", "line 7 column 11: unknown constant y")]
    [TestCase("(error \"first\")\n(error \"second\")\n", "first", TestName = "{m}(the first of two errors)")]
    [TestCase("(error \"line 1 column 2: it said \\\"no\\\" twice\")\n", "line 1 column 2: it said \\\"no\\\" twice", TestName = "{m}(a quote the solver wrote with a backslash)")]
    [TestCase("(error \"a message (with \"quotes\") inside\")\n", "a message (with \"quotes\") inside", TestName = "{m}(a quote and a parenthesis inside the message)")]
    [TestCase("(error \"two\nlines\")\n", "two\nlines")]
    [TestCase("(error \"not closed", "not closed")]
    [TestCase("(error : no position\")\n", "(error : no position\")", TestName = "{m}(the solver's other form is kept as it is)")]
    [TestCase("unsupported\n", "unsupported")]
    [TestCase("  \n", "")]
    public void TheMessageOfAnAnswerIsTheTextOfItsFirstErrorOrTheAnswerTest(string answer, string message)
    {
        Assert.That(SmtAnswers.Message(answer), Is.EqualTo(message));
    }

    [Test]
    public void AMessageIsCutToItsLimitAndNeverInsideACharacterTest()
    {
        var plain = SmtAnswers.Message("(error \"" + new string('a', 5_000) + "\")\n");

        // The character at the cut is one of two code units: the cut steps back before it.
        var pair = char.ConvertFromUtf32(0x1F600);
        var astral = SmtAnswers.Message("(error \"" + new string('a', SmtAnswers.MESSAGE_CHARACTERS - 1) + pair + "bbb\")\n");

        Assert.Multiple(() =>
        {
            Assert.That(plain, Has.Length.EqualTo(SmtAnswers.MESSAGE_CHARACTERS));
            Assert.That(astral, Is.EqualTo(new string('a', SmtAnswers.MESSAGE_CHARACTERS - 1)));
        });
    }

    #endregion

    #region Reason Tests

    [TestCase("(:reason-unknown \"canceled\")\n", "canceled")]
    [TestCase("(:reason-unknown \"max. resource limit exceeded\")\n", "max. resource limit exceeded")]
    [TestCase("(:reason-unknown \"(incomplete (theory arithmetic))\")\n", "(incomplete (theory arithmetic))")]
    [TestCase("(:reason-unknown incomplete)\n", "incomplete", TestName = "{m}(a symbol)")]
    [TestCase("(:reason-unknown (incomplete quantifiers))\n", "(incomplete quantifiers)", TestName = "{m}(a list)")]
    [TestCase("(:reason-unknown \"it said \\\"no\\\"\")\n", "it said \\\"no\\\"", TestName = "{m}(a quote the solver wrote with a backslash)")]
    [TestCase("no reason at all\n", "no reason at all", TestName = "{m}(another shape is kept as it is)")]
    public void TheReasonOfAnUnknownIsTheSolversWordTest(string answer, string reason)
    {
        Assert.That(SmtAnswers.Reason(answer), Is.EqualTo(reason));
    }

    #endregion

    #region Statistics Tests

    [TestCase("(:max-memory 9.29\n :memory 9.24\n :num-allocs 6256\n :rlimit-count 223\n :solve-eqs-steps 3)\n", 223L)]
    [TestCase("(:rlimit-count 4294967295)\n", 4_294_967_295L)]
    [TestCase("(:rlimit-count 5000000000.00)\n", 5_000_000_000L, TestName = "{m}(above 32 bits the solver writes two decimals)")]
    [TestCase("(:max-memory 8.64 :memory 8.64)\n", 0L, TestName = "{m}(a counter of zero is left out)")]
    [TestCase("()\n", 0L)]
    [TestCase("(:time -0.00 :rlimit-count 7)\n", 7L, TestName = "{m}(another value may be any atom)")]
    public void TheResourceCounterIsReadOutOfTheStatisticsTest(string answer, long count)
    {
        Assert.That(SmtAnswers.ResourceCount(answer), Is.EqualTo(count));
    }

    [TestCase("")]
    [TestCase("sat\n")]
    [TestCase("(:rlimit-count)\n")]
    [TestCase("(:rlimit-count 1 :memory)\n")]
    [TestCase("(rlimit-count 1)\n")]
    [TestCase("(:rlimit-count abc)\n")]
    [TestCase("(:rlimit-count (1))\n")]
    [TestCase("(:rlimit-count 1) (:rlimit-count 2)\n")]
    [TestCase("(:rlimit-count 99999999999999999999999999)\n")]
    [TestCase("(:rlimit-count 1")]
    public void StatisticsOfAnotherShapeAreUnreadableTest(string answer)
    {
        Assert.That(SmtAnswers.ResourceCount(answer), Is.Null);
    }

    #endregion

    #region Values Tests

    [Test]
    public void TheValuesOfTermsAreReadInTheOrderTheyWereAskedTest()
    {
        var terms = Terms("x", "(+ x y)", "|a b|");

        var values = SmtAnswers.Values("((x (- 7))\n ((+ x y) #x0f)\n (|a b| (/ 1.0 3.0)))\n", terms);

        Assert.That(values?.Select(value => value.ToString()), Is.EqualTo(new[] { "(- 7)", "#x0f", "(/ 1.0 3.0)" }));
    }

    [TestCase("((x 1))", TestName = "{m}(fewer values than terms)")]
    [TestCase("((x 1) (y 2) (z 3))", TestName = "{m}(more values than terms)")]
    [TestCase("((y 2) (x 1))", TestName = "{m}(another order)")]
    [TestCase("((x 1) (y))", TestName = "{m}(a term without a value)")]
    [TestCase("((x 1) y 2)", TestName = "{m}(no pair)")]
    [TestCase("(x 1) (y 2)", TestName = "{m}(two lists)")]
    [TestCase("((x 1) (y 2)", TestName = "{m}(a list that is not closed)")]
    [TestCase("sat", TestName = "{m}(a word)")]
    [TestCase("", TestName = "{m}(nothing)")]
    public void ValuesThatAreNotTheValuesOfTheTermsAreUnreadableTest(string answer)
    {
        Assert.That(SmtAnswers.Values(answer, Terms("x", "y")), Is.Null);
    }

    [Test]
    public void SilenceIsAnAnswerOfBlanksTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SmtAnswers.IsBlank(""), Is.True);
            Assert.That(SmtAnswers.IsBlank(" \r\n\t"), Is.True);
            Assert.That(SmtAnswers.IsBlank("unsupported\n"), Is.False);
        });
    }

    #endregion

    #region Tools

    private static List<SmtExpression> Terms(params string[] terms)
    {
        return terms.Select(term => SmtSanitizer.SanitizeTerm(term).Term!).ToList();
    }

    #endregion
}
