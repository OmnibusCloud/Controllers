using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// The shape of every kept command: how many parts, and what stands where a name must stand.
/// A sort or a term is any s-expression; what they mean is the solver's to check.
/// </summary>
[TestFixture]
public class SmtSanitizerShapeTests
{
    #region Constants

    private static readonly string[] WELL_FORMED =
    [
        "(set-logic QF_LIA)",
        "(set-logic |QF_LIA|)",
        "(set-logic ALL)",
        "(set-info :status sat)",
        "(set-info :smt-lib-version 2.6)",
        "(set-info :flag)",
        "(set-info :source |a b|)",
        "(set-info :note (a (b) \"c\" 1 :k))",
        "(declare-sort S)",
        "(declare-sort S 0)",
        "(declare-sort S 2)",
        "(define-sort T () Int)",
        "(define-sort T (A B) (Array A B))",
        "(declare-const x Int)",
        "(declare-const x (_ BitVec 8))",
        "(declare-const |assert| Int)",
        "(declare-const |a b| Int)",
        "(declare-const x ())",
        "(declare-fun f () Int)",
        "(declare-fun f (Int Bool) Int)",
        "(declare-fun f ((Array Int Int)) (_ BitVec 4))",
        "(define-fun f () Int 1)",
        "(define-fun f ((x Int) (y Bool)) Int (ite y x 0))",
        "(define-fun-rec f ((x Int)) Int (f x))",
        "(define-funs-rec ((f ((x Int)) Int)) ((f x)))",
        "(define-funs-rec ((f ((x Int)) Int) (g () Bool)) ((f x) true))",
        "(declare-datatype Color ((red) (green)))",
        "(declare-datatype L ((nil) (cons (hd Int) (tl L))))",
        "(declare-datatype P (par (T) ((mk (fst T) (snd T)))))",
        "(declare-datatypes ((L 0)) (((nil) (cons (hd Int) (tl L)))))",
        "(declare-datatypes ((A 0) (B 1)) (((a)) (par (T) ((b (v T))))))",
        "(assert true)",
        "(assert (! (> x 0) :named a1))",
        "(assert (let ((y 1)) (forall ((z Int)) (exists ((w Int)) (= y z w)))))",
        "(assert ())"
    ];

    private static readonly string[] MALFORMED =
    [
        "(set-logic)",
        "(set-logic QF_LIA ALL)",
        "(set-logic \"QF_LIA\")",
        "(set-logic (QF_LIA))",
        "(set-logic 5)",
        "(set-logic :QF_LIA)",
        "(set-info)",
        "(set-info status sat)",
        "(set-info :status sat unsat)",
        "(set-info \"status\")",
        "(set-info (:status sat))",
        "(declare-sort)",
        "(declare-sort S T)",
        "(declare-sort S 0 0)",
        "(declare-sort S 1.0)",
        "(declare-sort 5)",
        "(declare-sort (S))",
        "(define-sort T Int)",
        "(define-sort T ())",
        "(define-sort T (5) Int)",
        "(define-sort T ((A)) Int)",
        "(define-sort T A Int)",
        "(define-sort T () Int Int)",
        "(declare-const)",
        "(declare-const x)",
        "(declare-const x Int Int)",
        "(declare-const 5 Int)",
        "(declare-const \"x\" Int)",
        "(declare-const (x) Int)",
        "(declare-const :x Int)",
        "(declare-const #b01 Int)",
        "(declare-fun f Int)",
        "(declare-fun f Int Int)",
        "(declare-fun f ())",
        "(declare-fun f () Int Int)",
        "(declare-fun (f) () Int)",
        "(define-fun f () Int)",
        "(define-fun f Int 1)",
        "(define-fun f x Int 1)",
        "(define-fun f (x) Int 1)",
        "(define-fun f ((x)) Int 1)",
        "(define-fun f ((x Int Int)) Int 1)",
        "(define-fun f ((5 Int)) Int 1)",
        "(define-fun f (((x) Int)) Int 1)",
        "(define-fun f () Int 1 2)",
        "(define-fun 5 () Int 1)",
        "(define-fun-rec f () Int)",
        "(define-fun-rec f ((x)) Int 1)",
        "(define-funs-rec () ())",
        "(define-funs-rec ((f () Int)) ())",
        "(define-funs-rec ((f () Int)) (1 2))",
        "(define-funs-rec ((f () Int) (g () Int)) (1))",
        "(define-funs-rec (f) (1))",
        "(define-funs-rec ((f Int)) (1))",
        "(define-funs-rec ((f (x) Int)) (1))",
        "(define-funs-rec ((f () Int)) 1)",
        "(define-funs-rec ((f () Int)))",
        "(define-funs-rec ((5 () Int)) (1))",
        "(declare-datatype)",
        "(declare-datatype D)",
        "(declare-datatype D ())",
        "(declare-datatype D (red green))",
        "(declare-datatype D ((red) green))",
        "(declare-datatype D ((mk x)))",
        "(declare-datatype D ((mk (x))))",
        "(declare-datatype D ((mk (x Int Int))))",
        "(declare-datatype D ((mk (5 Int))))",
        "(declare-datatype D ((5)))",
        "(declare-datatype D (()))",
        "(declare-datatype D (par (T)))",
        "(declare-datatype D (par () ((mk))))",
        "(declare-datatype D (par (T) ()))",
        "(declare-datatype D (par T ((mk))))",
        "(declare-datatype D (par (5) ((mk))))",
        "(declare-datatype D (par (T) ((mk)) x))",
        "(declare-datatype D ((mk)) ((mk2)))",
        "(declare-datatype 5 ((a)))",
        "(declare-datatypes () ())",
        "(declare-datatypes ((D 0)) ())",
        "(declare-datatypes ((D 0)) (((a)) ((b))))",
        "(declare-datatypes ((D 0) (E 0)) (((a))))",
        "(declare-datatypes ((D)) (((a))))",
        "(declare-datatypes ((D x)) (((a))))",
        "(declare-datatypes ((D 0 0)) (((a))))",
        "(declare-datatypes (D) (((a))))",
        "(declare-datatypes ((D 0)) ((a)))",
        "(declare-datatypes ((D 0)) (a))",
        "(declare-datatypes ((D 0)))",
        "(declare-datatypes D (((a))))",
        "(assert)",
        "(assert true false)"
    ];

    /// <summary>A general reserved word that is not between bars is no symbol, so no name.</summary>
    private static readonly string[] RESERVED_WORD_AS_NAME =
    [
        "(set-logic let)",
        "(declare-sort par)",
        "(define-sort T (match) Int)",
        "(declare-const _ Int)",
        "(declare-const ! Int)",
        "(declare-const NUMERAL Int)",
        "(declare-fun forall () Int)",
        "(define-fun exists () Int 1)",
        "(define-fun f ((let Int)) Int 1)",
        "(define-fun-rec as () Int 1)",
        "(define-funs-rec ((let () Int)) (1))",
        "(define-funs-rec ((f ((HEXADECIMAL Int)) Int)) (1))",
        "(declare-datatype STRING ((a)))",
        "(declare-datatype D ((forall)))",
        "(declare-datatype D ((mk (exists Int))))",
        "(declare-datatype D (par (DECIMAL) ((mk))))",
        "(declare-datatypes ((BINARY 0)) (((a))))"
    ];

    #endregion

    #region Shape Tests

    [TestCaseSource(nameof(WellFormed))]
    public void WellFormedCommandIsKeptAsWrittenTest(string text)
    {
        var problem = SmtSanitizerChecks.Accept(text);

        Assert.That(problem.Canonical, Is.EqualTo(text.Replace("|QF_LIA|", "QF_LIA") + "\n"));
        SmtSanitizerChecks.HoldProperties(problem);
    }

    [TestCaseSource(nameof(Malformed))]
    public void MalformedCommandRefusesTheProblemTest(string text)
    {
        var head = SmtReader.Read(text, SmtReaderLimits.Problem).Single().Head;

        var refusal = SmtSanitizerChecks.Refuse($"; a comment\n(declare-const before Int)\n  {text}\n(assert true)", SmtRefusalCodes.COMMAND_MALFORMED);

        Assert.That(refusal.Command, Is.EqualTo(head));
        Assert.That(refusal.Line, Is.EqualTo(3));
        Assert.That(refusal.Reason, Does.StartWith($"the command is written ({head}"));
    }

    [TestCaseSource(nameof(ReservedWordAsName))]
    public void ReservedWordWhereANameMustStandRefusesTheProblemTest(string text)
    {
        var head = SmtReader.Read(text, SmtReaderLimits.Problem).Single().Head;

        var refusal = SmtSanitizerChecks.Refuse(text, SmtRefusalCodes.COMMAND_MALFORMED);

        Assert.That(refusal.Command, Is.EqualTo(head));
        Assert.That(refusal.Reason, Does.StartWith($"a reserved word stands where a name must stand; the command is written ({head}"));
    }

    [Test]
    public void CommandNameIsAnOrdinarySymbolEverywhereButAtTheHeadOfACommandTest()
    {
        // A solver takes exit, push or assert as a name wherever it looks for no command, and
        // tools that print for it write them bare: read as symbols, they are read as the solver
        // reads them. Bare or between bars, as the name of a constant, a function, a sort, a
        // datatype, a constructor, a selector, a parameter and a bound variable, and in a term.
        foreach (var word in SmtReservedWords.Commands)
        {
            foreach (var name in new[] { word, $"|{word}|" })
            {
                var text =
                    $"(set-logic {name})\n" +
                    $"(declare-const {name} Int)\n" +
                    $"(declare-fun {name} (Int {name}) {name})\n" +
                    $"(declare-sort {name} 0)\n" +
                    $"(define-sort {name} ({name}) (Array {name} Int))\n" +
                    $"(define-fun {name} (({name} Int)) Int {name})\n" +
                    $"(define-fun-rec {name} (({name} Int)) Int ({name} {name}))\n" +
                    $"(define-funs-rec (({name} (({name} Int)) Int)) (({name} {name})))\n" +
                    $"(declare-datatype {name} (({name} ({name} Int))))\n" +
                    $"(declare-datatypes (({name} 0)) ((({name} ({name} Int)))))\n" +
                    $"(declare-datatype P (par ({name}) ((mk (get {name})))))\n" +
                    $"(assert (> {name} 0))\n" +
                    $"(assert (let (({name} 1)) (forall (({name} Int)) (= {name} ({name} {name})))))\n" +
                    $"(assert (! {name} :named {name}))\n";

                var problem = SmtSanitizerChecks.Accept(text);

                Assert.That(problem.Canonical, Is.EqualTo(text), name);
                Assert.That(problem.Logic, Is.EqualTo(word), name);
                Assert.That(problem.Constants.Single().Name, Is.EqualTo(word), name);
                Assert.That(problem.Functions.Single().Name, Is.EqualTo(word), name);
                Assert.That(problem.DeclaredSymbolCount, Is.EqualTo(16), name);
                SmtSanitizerChecks.HoldProperties(problem);
            }
        }
    }

    [Test]
    public void CommandNameIsWrittenBackAsItWasReadTest()
    {
        // Bare and between bars it is one symbol to a solver; the text keeps each spelling.
        var problem = SmtSanitizerChecks.Accept("(declare-const exit Bool)\n(declare-const |push| Bool)\n(assert (and |exit| push))");

        Assert.That(problem.Canonical, Is.EqualTo("(declare-const exit Bool)\n(declare-const |push| Bool)\n(assert (and |exit| push))\n"));
        Assert.That(problem.Constants.Select(constant => constant.Name), Is.EqualTo(new[] { "exit", "push" }));
        SmtSanitizerChecks.HoldProperties(problem);
    }

    [Test]
    public void CommandNameAtTheHeadOfACommandIsTheCommandTest()
    {
        // Only there: a top-level form is looked up by its head.
        Assert.That(SmtSanitizerChecks.Refuse("(declare-const exit Bool)\n(push 1)", SmtRefusalCodes.COMMAND_NOT_ALLOWED).Line, Is.EqualTo(2));
        Assert.That(SmtSanitizerChecks.Accept("(declare-const exit Bool)\n(assert exit)\n(exit)").Stripped["exit"], Is.EqualTo(1));
    }

    [Test]
    public void ShapeIsCheckedAfterTheTokensOfTheCommandTest()
    {
        // One command, two faults: the token is named first, it is the nearer thing to mend.
        Assert.That(SmtSanitizerChecks.Refuse("(declare-const |let|)", SmtRefusalCodes.RESERVED_SYMBOL).Command, Is.EqualTo("declare-const"));
        Assert.That(SmtSanitizerChecks.Refuse("(assert -5 x)", SmtRefusalCodes.AMBIGUOUS_TOKEN).Command, Is.EqualTo("assert"));
    }

    #endregion

    #region Declared Name Tests

    [Test]
    public void EveryDeclaredNameIsCountedTest()
    {
        const string text =
            "(declare-sort S 0)\n" +                                                 // 1
            "(define-sort T (A) (Array A A))\n" +                                    // 1: A is a parameter
            "(declare-const a Int)\n" +                                              // 1
            "(declare-fun f (Int) Int)\n" +                                          // 1
            "(define-fun g ((x Int)) Int x)\n" +                                     // 1: x is bound
            "(define-fun-rec h ((x Int)) Int x)\n" +                                 // 1
            "(define-funs-rec ((p ((x Int)) Int) (q () Int)) (1 2))\n" +             // 2
            "(declare-datatype D ((mk (sel Int)) (nil)))\n" +                        // 1 + 2 + 1
            "(declare-datatypes ((E 0) (F 1)) (((e)) (par (X) ((f2 (v X))))))\n" +   // 2 + 1 + 2
            "(assert (let ((y 1)) (forall ((z Int)) (= y z))))\n" +                  // 0
            "(set-info :status sat)\n";                                              // 0

        var problem = SmtSanitizerChecks.Accept(text);

        Assert.That(problem.DeclaredSymbolCount, Is.EqualTo(17));
        SmtSanitizerChecks.HoldProperties(problem);
    }

    [TestCase("(declare-const oc!x Int)")]
    [TestCase("(declare-const |oc!x| Int)")]
    [TestCase("(declare-const |oc! with a blank| Int)")]
    [TestCase("(declare-const oc! Int)")]
    [TestCase("(declare-fun oc!f (Int) Int)")]
    [TestCase("(define-fun oc!f () Int 1)")]
    [TestCase("(define-fun-rec oc!f () Int 1)")]
    [TestCase("(define-funs-rec ((f () Int) (oc!g () Int)) (1 2))")]
    [TestCase("(declare-sort oc!S 0)")]
    [TestCase("(define-sort oc!T () Int)")]
    [TestCase("(declare-datatype oc!D ((a)))")]
    [TestCase("(declare-datatype D ((oc!a)))")]
    [TestCase("(declare-datatype D ((a (oc!sel Int))))")]
    [TestCase("(declare-datatype D (par (T) ((a (oc!sel T)))))")]
    [TestCase("(declare-datatypes ((oc!D 0)) (((a))))")]
    [TestCase("(declare-datatypes ((D 0)) (((a) (oc!b))))")]
    [TestCase("(declare-datatypes ((D 0)) (((a (oc!sel Int)))))")]
    public void DeclaredNameWithTheControllersPrefixRefusesTheProblemTest(string text)
    {
        var head = SmtReader.Read(text, SmtReaderLimits.Problem).Single().Head;

        var refusal = SmtSanitizerChecks.Refuse($"(set-logic ALL)\n{text}", SmtRefusalCodes.RESERVED_PREFIX);

        Assert.That(refusal.Command, Is.EqualTo(head));
        Assert.That(refusal.Line, Is.EqualTo(2));
        Assert.That(refusal.Reason, Does.Contain("'oc!'"));
    }

    [Test]
    public void ThePrefixOfALongNameIsShownCutTest()
    {
        var refusal = SmtSanitizerChecks.Refuse($"(declare-const oc!{new string('q', 5_000)} Int)", SmtRefusalCodes.RESERVED_PREFIX);

        Assert.That(refusal.Reason, Does.Contain("'oc!" + new string('q', 58) + "...'"));
        Assert.That(refusal.Reason, Does.Not.Contain(new string('q', 59)));
    }

    #endregion

    #region Tools

    private static IEnumerable<string> WellFormed()
    {
        return WELL_FORMED;
    }

    private static IEnumerable<string> Malformed()
    {
        return MALFORMED;
    }

    private static IEnumerable<string> ReservedWordAsName()
    {
        return RESERVED_WORD_AS_NAME;
    }

    #endregion
}
