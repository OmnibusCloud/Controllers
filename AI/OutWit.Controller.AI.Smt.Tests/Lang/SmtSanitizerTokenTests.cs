using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// The rules every atom of a kept command is held to: the places where a text that is good
/// SMT-LIB means something else to the solver, or asks it for something no problem needs.
/// </summary>
[TestFixture]
public class SmtSanitizerTokenTests
{
    #region Reserved Symbol Tests

    [Test]
    public void GeneralReservedWordBetweenBarsRefusesTheProblemWhereverItStandsTest()
    {
        foreach (var word in SmtReservedWords.General)
        {
            var places = new (string Text, string Command, int Line)[]
            {
                ($"(declare-const |{word}| Int)", "declare-const", 1),
                ($"(declare-const x |{word}|)", "declare-const", 1),
                ($"(declare-fun f (Int |{word}|) Int)", "declare-fun", 1),
                ($"(define-fun f ((|{word}| Int)) Int 1)", "define-fun", 1),
                ($"(assert\n (and a\n  (or b (|{word}| c))))", "assert", 3),
                ($"(assert |{word}|)", "assert", 1),
                ($"(set-logic |{word}|)", "set-logic", 1),
                ($"(declare-datatype D ((mk (|{word}| Int))))", "declare-datatype", 1)
            };

            foreach (var (text, command, line) in places)
            {
                var refusal = SmtSanitizerChecks.Refuse(text, SmtRefusalCodes.RESERVED_SYMBOL);

                Assert.That(refusal.Command, Is.EqualTo(command), text);
                Assert.That(refusal.Line, Is.EqualTo(line), text);
                Assert.That(refusal.Reason, Does.StartWith($"the reserved word '{word}' is written between bars"), text);
            }
        }
    }

    [Test]
    public void GeneralReservedWordWithoutBarsIsTheSolversToJudgeTest()
    {
        // Without bars the word is the reserved word to everybody: one reading, so no refusal here.
        var problem = SmtSanitizerChecks.Accept("(assert (let ((x 1)) (forall ((y Int)) (exists ((z Int)) (! (= x y z) :named n)))))\n(assert ((_ extract 3 0) (as v (_ BitVec 8))))\n(assert (match l ((nil true))))\n(assert (par NUMERAL DECIMAL STRING BINARY HEXADECIMAL))");

        Assert.That(problem.AssertionCount, Is.EqualTo(4));
        SmtSanitizerChecks.HoldProperties(problem);
    }

    [Test]
    public void NameThatOnlyHoldsAReservedWordIsANameTest()
    {
        var problem = SmtSanitizerChecks.Accept("(declare-const |let x| Int)\n(declare-const lets Int)\n(declare-const |Let| Int)\n(declare-const |_x| Int)");

        Assert.That(problem.Canonical, Is.EqualTo("(declare-const |let x| Int)\n(declare-const lets Int)\n(declare-const Let Int)\n(declare-const _x Int)\n"));
    }

    #endregion

    #region Ambiguous Token Tests

    [TestCase("(assert (> x -5))", "-5")]
    [TestCase("(assert (> x -0))", "-0")]
    [TestCase("(assert (> x -1.5))", "-1.5")]
    [TestCase("(assert (> x -5x))", "-5x")]
    [TestCase("(assert (> x -5-5))", "-5-5")]
    [TestCase("(assert (> x |-5|))", "-5")]
    [TestCase("(declare-const -5 Int)", "-5")]
    [TestCase("(declare-const |-7| Int)", "-7")]
    [TestCase("(declare-const x -1)", "-1")]
    [TestCase("(assert -5)", "-5")]
    public void SymbolThatStartsWithAMinusAndADigitRefusesTheProblemTest(string text, string token)
    {
        var refusal = SmtSanitizerChecks.Refuse("\n" + text, SmtRefusalCodes.AMBIGUOUS_TOKEN);

        Assert.That(refusal.Line, Is.EqualTo(2));
        Assert.That(refusal.Command, Is.EqualTo(SmtReader.Read(text, SmtReaderLimits.Problem).Single().Head));
        Assert.That(refusal.Reason, Does.StartWith($"'{token}' is a symbol by the standard and a negative number to the solver"));
    }

    [TestCase("(assert (> x (- 5)))")]
    [TestCase("(assert (> x (- 5 3)))")]
    [TestCase("(assert (> x -))")]
    [TestCase("(assert (> x -x))")]
    [TestCase("(assert (> x --5))")]
    [TestCase("(assert (> x x-5))")]
    [TestCase("(assert (> x +5))")]
    [TestCase("(assert (> x -.5))")]
    [TestCase("(assert (> x |-5 a|))")]
    [TestCase("(assert (> x |- 5|))")]
    [TestCase("(assert (= s \"-5\"))")]
    public void MinusElsewhereIsNoNumberToAnybodyTest(string text)
    {
        SmtSanitizerChecks.HoldProperties(SmtSanitizerChecks.Accept(text));
    }

    [Test]
    public void LongAmbiguousTokenIsShownCutTest()
    {
        var refusal = SmtSanitizerChecks.Refuse($"(assert (> x -5{new string('9', 5_000)}x))", SmtRefusalCodes.AMBIGUOUS_TOKEN);

        Assert.That(refusal.Reason, Does.StartWith("'-5" + new string('9', 59) + "...' is a symbol"));
        Assert.That(refusal.Reason, Does.Not.Contain(new string('9', 60)));
    }

    #endregion

    #region String Tests

    [Test]
    public void StringWithACharacterOutsideAsciiRefusesTheProblemTest()
    {
        var cases = new (string Content, string Reason)[]
        {
            ("caf\u00e9", "U+00E9: a character outside printable ASCII is written as the escape \\u{E9}"),
            ("\u00a0", "U+00A0: a character outside printable ASCII is written as the escape \\u{A0}"),
            ("a\u2028b", "U+2028: a character outside printable ASCII is written as the escape \\u{2028}"),
            ("\ud83d\ude00", "U+1F600: a character outside printable ASCII is written as the escape \\u{1F600}"),
            ("plain \ufffd", "U+FFFD: a character outside printable ASCII is written as the escape \\u{FFFD}")
        };

        foreach (var (content, reason) in cases)
        {
            foreach (var (text, command) in new[] { ($"(assert\n (= s \"{content}\"))", "assert"), ($"(define-fun f () String\n \"{content}\")", "define-fun") })
            {
                var refusal = SmtSanitizerChecks.Refuse(text, SmtRefusalCodes.NON_ASCII_STRING);

                Assert.That(refusal.Line, Is.EqualTo(2), reason);
                Assert.That(refusal.Command, Is.EqualTo(command), reason);
                Assert.That(refusal.Reason, Is.EqualTo("a string literal holds the character " + reason));
            }
        }
    }

    [Test]
    public void StringOfPrintableAsciiTabsAndLineBreaksIsKeptTest()
    {
        var printable = string.Concat(Enumerable.Range(32, 95).Select(code => (char)code)).Replace("\"", "\"\"");

        var problem = SmtSanitizerChecks.Accept($"(assert (= s \"{printable}\"))\n(assert (= t \"a\tb\r\nc\nd\"))\n(assert (= u \"\\u{{e9}} \\x41 \\\\\"))");

        Assert.That(problem.Canonical, Does.Contain($"\"{printable}\""));
        Assert.That(problem.Canonical, Does.Contain("\"a\tb\r\nc\nd\""));
        Assert.That(problem.Canonical, Does.Contain("\"\\u{e9} \\x41 \\\\\""));
        SmtSanitizerChecks.HoldProperties(problem);
    }

    [Test]
    public void CharacterOutsideAsciiInAQuotedSymbolIsKeptTest()
    {
        // The rule is the theory of strings': a symbol is a name, whatever it is made of.
        var problem = SmtSanitizerChecks.Accept("(declare-const |caf\u00e9 \ud83d\ude00| Int)\n(set-info :source |\u00a9 2025 the authors|)");

        Assert.That(problem.Canonical, Is.EqualTo("(declare-const |caf\u00e9 \ud83d\ude00| Int)\n(set-info :source |\u00a9 2025 the authors|)\n"));
        Assert.That(problem.Source, Is.EqualTo("\u00a9 2025 the authors"));
        SmtSanitizerChecks.HoldProperties(problem);
    }

    #endregion

    #region Index Tests

    [TestCase("(declare-const x (_ BitVec 65537))", "65537")]
    [TestCase("(declare-const x (_ BitVec 1048576))", "1048576")]
    [TestCase("(assert (= x ((_ zero_extend 99999) y)))", "99999")]
    [TestCase("(assert (= x ((_ sign_extend 65537) y)))", "65537")]
    [TestCase("(assert (= x ((_ repeat 100000) y)))", "100000")]
    [TestCase("(assert (= x ((_ rotate_left 4294967296) y)))", "4294967296")]
    [TestCase("(assert (= x ((_ rotate_right 70000) y)))", "70000")]
    [TestCase("(assert (= x ((_ extract 70000 0) y)))", "70000")]
    [TestCase("(assert (= x ((_ extract 7 99999) y)))", "99999")]
    [TestCase("(assert (= x (_ bv0 70000)))", "70000")]
    [TestCase("(assert ((_ divisible 65537) x))", "65537")]
    [TestCase("(define-fun f ((x (_ BitVec 99999))) Bool true)", "99999")]
    public void IndexAboveTheWidestBitVectorRefusesTheProblemTest(string text, string index)
    {
        var refusal = SmtSanitizerChecks.Refuse("(set-logic QF_BV)\n" + text, SmtRefusalCodes.INDEX_TOO_LARGE);

        Assert.That(refusal.Line, Is.EqualTo(2));
        Assert.That(refusal.Command, Is.EqualTo(SmtReader.Read(text, SmtReaderLimits.Problem).Single().Head));
        Assert.That(refusal.Reason, Is.EqualTo($"the index {index} is above the 65536 allowed"));
    }

    [TestCase("(declare-const x (_ BitVec 65536))")]
    [TestCase("(declare-const x (_ BitVec 1))")]
    [TestCase("(assert (= x ((_ zero_extend 65536) y)))")]
    [TestCase("(assert (= x ((_ extract 65535 0) y)))")]
    [TestCase("(assert (= x (_ bv4294967296 64)))")]
    [TestCase("(assert (> x 1048576))")]
    [TestCase("(assert (= x (+ 99999 70000)))")]
    [TestCase("(assert (= x (_ a (99999))))")]
    [TestCase("(assert (= x (f _ 99999)))")]
    [TestCase("(assert (= x (_ a 99999.0)))")]
    [TestCase("(assert (= x (_ a #xFFFFFFFF)))")]
    [TestCase("(assert (= x (_ char #x1F600)))")]
    public void NumeralThatIsNoIndexOrIsWithinTheLimitIsKeptTest(string text)
    {
        SmtSanitizerChecks.HoldProperties(SmtSanitizerChecks.Accept(text));
    }

    [Test]
    public void LongIndexIsComparedWithoutBeingParsedAndShownCutTest()
    {
        var refusal = SmtSanitizerChecks.Refuse($"(declare-const x (_ BitVec 1{new string('0', 4_000)}))", SmtRefusalCodes.INDEX_TOO_LARGE);

        Assert.That(refusal.Reason, Is.EqualTo("the index 1" + new string('0', 60) + "... is above the 65536 allowed"));
    }

    #endregion

    #region Information Tests

    [Test]
    public void InformationIsNotHeldToTheTokenRulesTest()
    {
        // The rules are about what the solver reads, and no set-info reaches it: an author's
        // name outside ASCII, or anything else of a piece of information, is kept as read.
        const string text =
            "(set-info :source \"S\u00f8ren Kierkeg\u00e5rd, \u00a9 2025 \ud83d\ude00\")\n" +
            "(set-info :license |let|)\n" +
            "(set-info :version -1)\n" +
            "(set-info :note (_ a 99999))\n" +
            "(set-info :authors (|forall| \"\u00e9\" -5 (_ b 1048576) |_| |-7|))\n" +
            "(assert true)\n";

        var problem = SmtSanitizerChecks.Accept(text);

        Assert.That(problem.Canonical, Is.EqualTo(text.Replace("|-7|", "-7")));
        Assert.That(problem.SolverText, Is.EqualTo("(assert true)\n"));
        Assert.That(problem.Source, Is.EqualTo("S\u00f8ren Kierkeg\u00e5rd, \u00a9 2025 \ud83d\ude00"));
        Assert.That(problem.License, Is.EqualTo("let"));
        Assert.That(problem.Hash, Is.EqualTo(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(problem.Canonical)))));
        SmtSanitizerChecks.HoldProperties(problem);
    }

    [Test]
    public void InformationIsStillHeldToItsShapeTest()
    {
        Assert.That(SmtSanitizerChecks.Refuse("(set-info \"\u00e9\")", SmtRefusalCodes.COMMAND_MALFORMED).Command, Is.EqualTo("set-info"));
        Assert.That(SmtSanitizerChecks.Refuse("(set-info |let| 1)", SmtRefusalCodes.COMMAND_MALFORMED).Command, Is.EqualTo("set-info"));
        Assert.That(SmtSanitizerChecks.Refuse("(set-info :a -5 -6)", SmtRefusalCodes.COMMAND_MALFORMED).Command, Is.EqualTo("set-info"));
    }

    [Test]
    public void EveryOtherKeptCommandIsHeldToTheTokenRulesTest()
    {
        // set-logic is the solver's too: only set-info is left out.
        SmtSanitizerChecks.Refuse("(set-logic |let|)", SmtRefusalCodes.RESERVED_SYMBOL);
        SmtSanitizerChecks.Refuse("(set-logic -5)", SmtRefusalCodes.AMBIGUOUS_TOKEN);
        SmtSanitizerChecks.Refuse("(declare-sort |-5| 0)", SmtRefusalCodes.AMBIGUOUS_TOKEN);
        SmtSanitizerChecks.Refuse("(define-sort T () (_ BitVec 99999))", SmtRefusalCodes.INDEX_TOO_LARGE);
        SmtSanitizerChecks.Refuse("(declare-datatype D ((mk (get (_ BitVec 99999)))))", SmtRefusalCodes.INDEX_TOO_LARGE);
        SmtSanitizerChecks.Refuse("(define-funs-rec ((f () String)) (\"\u00e9\"))", SmtRefusalCodes.NON_ASCII_STRING);
    }

    #endregion

    #region Order Tests

    [Test]
    public void TheFirstAtomOfTheTextThatBreaksARuleIsTheOneRefusedTest()
    {
        Assert.That(SmtSanitizerChecks.Refuse("(assert (f (g |let|) -5 \"\u00e9\" (_ b 99999)))", SmtRefusalCodes.RESERVED_SYMBOL).Line, Is.EqualTo(1));
        Assert.That(SmtSanitizerChecks.Refuse("(assert (f (g -5) |let| \"\u00e9\" (_ b 99999)))", SmtRefusalCodes.AMBIGUOUS_TOKEN).Line, Is.EqualTo(1));
        Assert.That(SmtSanitizerChecks.Refuse("(assert (f (g \"\u00e9\") -5 |let| (_ b 99999)))", SmtRefusalCodes.NON_ASCII_STRING).Line, Is.EqualTo(1));
        Assert.That(SmtSanitizerChecks.Refuse("(assert (f ((_ b 99999)) \"\u00e9\" -5 |let|))", SmtRefusalCodes.INDEX_TOO_LARGE).Line, Is.EqualTo(1));
        Assert.That(SmtSanitizerChecks.Refuse("(assert (f (|_| b 99999)))", SmtRefusalCodes.RESERVED_SYMBOL).Line, Is.EqualTo(1));
    }

    [Test]
    public void StrippedAndRefusedCommandsAreNotHeldToTheTokenRulesTest()
    {
        // What is not kept is not looked into: a stripped command is dropped whatever it holds,
        // a refused one is refused by its name.
        var problem = SmtSanitizerChecks.Accept("(get-value (-5 |let| \"\u00e9\" (_ b 99999)))");

        Assert.That(problem.Stripped["get-value"], Is.EqualTo(1));
        Assert.That(SmtSanitizerChecks.Refuse("(echo -5 |let|)", SmtRefusalCodes.COMMAND_NOT_ALLOWED).Command, Is.EqualTo("echo"));
    }

    [Test]
    public void TokenRulesWalkATermAsDeepAsTheReaderTakesTest()
    {
        var open = new string('(', SmtReaderLimits.PROBLEM_DEPTH - 1);
        var close = new string(')', SmtReaderLimits.PROBLEM_DEPTH - 1);

        SmtSanitizerChecks.Refuse($"(assert {open}-5{close})", SmtRefusalCodes.AMBIGUOUS_TOKEN);
        SmtSanitizerChecks.Accept($"(assert {open}x{close})");
    }

    #endregion
}
