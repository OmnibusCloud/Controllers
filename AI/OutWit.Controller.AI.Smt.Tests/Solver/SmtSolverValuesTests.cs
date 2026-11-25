using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// What the host's check of a row's values stands on, measured on the real solver module: the
/// solver echoes every term whose value it is asked for as the term was written to it, so the
/// pairs of an honest answer read as the terms asked; its model defines every symbol the
/// problem declares, used by an assertion or not, so no value of an honest row has to complete
/// the model; and an honest row with values is of the class its model alone is of, in every
/// theory the evaluator computes, while a value the evaluator does not compute leaves the row
/// partial and never failed. Needs the module (<see cref="SmtSolverModule"/>).
/// </summary>
[TestFixture]
[Category("Solver")]
public sealed class SmtSolverValuesTests
{
    #region Constants

    private const string LIST = "(declare-datatypes ((L 0)) (((nil) (cons (hd Int) (tl L)))))";
    private const char TAB = (char)9;
    private const char LINE_FEED = (char)10;

    /// <summary>Problems with terms of every spelling a term has, each with the terms asked of it.</summary>
    private static readonly (string Problem, string[] Terms)[] SPELLINGS =
    [
        ("(set-logic QF_LIA)(declare-const |a b| Int)(declare-const abc Int)(assert (> |a b| abc))", ["|a b|", "|abc|", "(+ |a b| |abc|)", "(-   abc)", "(- 5)", "(let ((z abc)) (+ z 1))", "(! abc :named n)"]),
        ("(set-logic QF_BV)(declare-const p (_ BitVec 4))(assert (= p #b0101))", ["p", "#b0101", "#x0F", "(_ bv5 4)", "(bvadd p #b0001)", "((_ zero_extend 4) p)"]),
        ("(set-logic QF_LRA)(declare-const x Real)(assert (= (* 3.0 x) 1.0))", ["x", "1.50", "(/ x 2.0)", "(* 2 x)", "(to_int x)", "3"]),
        ("(set-logic ALL)(declare-const s String)(assert (= s \"q\"\"r\"))", ["s", "(str.++ \"a\"\"b\" s)", "(str.len s)", "(str.++ s (str.from_code 7) (str.from_code 92))"]),
        ("(set-logic QF_AUFLIA)(declare-const a (Array Int Int))(declare-fun f (Int) Int)(assert (= (select a 1) 5))(assert (= (f 2) 7))", ["a", "(select a 1)", "(store a 2 3)", "(f 2)", "((as const (Array Int Int)) 4)"]),
        ("(set-logic ALL)" + LIST + "(declare-const l L)(assert (= (hd l) 4))(assert ((_ is cons) l))", ["l", "(tl l)", "(cons 1 l)", "((_ is nil) l)", "(match l ((nil 0) ((cons h t) h)))"])
    ];

    /// <summary>Problems that declare symbols no assertion mentions, in the logics a model's printing differs by.</summary>
    private static readonly string[] UNUSED =
    [
        "(set-logic QF_LIA)(declare-const x Int)(declare-const y Int)(assert (> x 0))",
        "(set-logic QF_BV)(declare-const p (_ BitVec 4))(declare-const unused (_ BitVec 4))(assert (= p #b0101))",
        "(set-logic QF_UFLIA)(declare-const x Int)(declare-fun g (Int) Int)(declare-fun h (Int Int) Bool)(assert (> x 3))",
        "(set-logic QF_AX)(declare-sort I 0)(declare-sort E 0)(declare-const a (Array I E))(declare-const i I)(assert (= i i))",
        "(set-logic QF_UF)(declare-sort U 0)(declare-const u U)(declare-fun k (U) U)(declare-const p Bool)(assert p)",
        "(set-logic ALL)" + LIST + "(declare-const l L)(declare-const s String)(declare-const r Real)(declare-const b Bool)(assert true)",
        "(declare-const x Int)(declare-const y Int)(assert (> (* x x) 1))"
    ];

    /// <summary>Checks that ask for values the evaluator computes, a theory each.</summary>
    private static readonly (string Name, string Problem, string[] Terms)[] COMPUTED =
    [
        ("integers", SmtSolverCases.LIA, ["x", "(+ x y)", "(* 2 z)", "(- z)", "(mod x 3)", "(ite (> x y) x y)"]),
        ("reals", SmtSolverCases.LRA, ["r", "s", "t", "(/ r 2.0)", "(- r)", "(+ r s t)", "(to_int (* 6.0 r))", "(< t r)"]),
        ("bit-vectors", SmtSolverCases.BV, ["p", "q", "(bvadd p q)", "((_ extract 3 0) p)", "(concat p q)", "((_ sign_extend 8) p)", "(bvult q p)"]),
        ("strings", "(set-logic ALL)(declare-const s String)(declare-const n Int)(assert (= s \"q\"\"r\"))(assert (= n (str.len s)))", ["s", "n", "(str.++ s \"a\")", "(str.at s 1)", "(str.len (str.++ s s))"]),
        ("arrays", "(set-logic QF_AUFLIA)(declare-const a (Array Int Int))(declare-const b (Array Int Int))(assert (= (select a 1) 5))(assert (= b (store a 2 3)))", ["a", "b", "(select b 2)", "(store b 1 5)", "(= a b)"]),
        ("arrays of bit-vectors", "(set-logic QF_ABV)(declare-const a (Array (_ BitVec 2) (_ BitVec 2)))(assert (= (select a #b01) #b10))(assert (= (select a #b10) #b11))", ["a", "(select a #b01)", "(store a #b00 #b01)"]),
        ("functions", "(set-logic QF_UFLIA)(declare-fun f (Int) Int)(declare-fun g (Int Int) Bool)(declare-const x Int)(assert (= (f x) 7))(assert (g x (f 3)))(assert (> x 0))", ["x", "(f x)", "(f 3)", "(f 99)", "(g x (f 3))", "(g 0 0)"]),
        ("uninterpreted sorts", "(set-logic QF_UF)(declare-sort U 0)(declare-const u U)(declare-const w U)(declare-fun k (U) U)(assert (distinct u w))(assert (= (k u) w))", ["u", "w", "(k u)", "(k w)", "(= u w)", "(k (k u))"]),
        ("arrays over uninterpreted sorts", "(set-logic QF_AX)(declare-sort I 0)(declare-sort E 0)(declare-const a (Array I E))(declare-const i I)(declare-const e E)(assert (= (select a i) e))", ["a", "i", "e", "(select a i)"]),
        ("datatypes", "(set-logic ALL)" + LIST + "(declare-const l L)(declare-const e L)(assert (= (hd l) 4))(assert ((_ is cons) l))", ["l", "e", "(tl l)", "(cons 1 l)", "((_ is nil) e)", "(hd (cons 9 e))"]),
        ("definitions and named terms", "(set-logic QF_LIA)(declare-const x Int)(define-fun c () Int 5)(define-fun twice ((n Int)) Int (* 2 n))(assert (! (> x c) :named big))", ["c", "big", "(+ c x)", "(twice x)"]),
        ("symbols no assertion mentions", "(set-logic QF_UFLIA)(declare-const x Int)(declare-const y Int)(declare-fun g (Int) Int)(assert (> x 0))", ["y", "(+ x y)", "(g x)", "x"])
    ];

    /// <summary>Checks that ask for a value the solver gives and the evaluator does not compute, each with the evaluator's reason.</summary>
    private static readonly (string Name, string Problem, string Term, string Reason)[] NOT_COMPUTED =
    [
        ("a division by zero", SmtSolverCases.LIA, "(div x 0)", SmtValidationReasons.DIVISION_BY_ZERO),
        ("a selector of another constructor", "(set-logic ALL)" + LIST + "(declare-const l L)(assert ((_ is cons) l))", "(hd nil)", SmtValidationReasons.SELECTOR_OF_OTHER_CONSTRUCTOR),
        ("a quantified term", "(set-logic ALL)(declare-fun f (Int) Int)(assert (= (f 1) 0))", "(forall ((i Int)) (>= (f i) 0))", SmtValidationReasons.QUANTIFIER),
        ("an algebraic number", "(set-logic QF_NRA)(declare-const x Real)(assert (= (* x x) 2.0))(assert (> x 0.0))", "(+ x 1.0)", SmtValidationReasons.ALGEBRAIC_NUMBER),
        ("a floating-point number", "(set-logic QF_FP)(declare-const f (_ FloatingPoint 8 24))(assert (fp.gt f ((_ to_fp 8 24) RNE 1.0)))", "(fp.add RNE f f)", SmtValidationReasons.FLOATING_POINT),
        ("a string literal with a tab and a line break", "(set-logic ALL)(declare-const s String)(assert (= s \"q\"))", "(str.++ s \"a" + TAB + "b" + LINE_FEED + "c\")", SmtValidationReasons.STRING_LITERAL)
    ];

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        SmtSolverModule.Require();
        SmtSolverRig.Warm();
    }

    #endregion

    #region Echo Tests

    [Test]
    public void TheSolverEchoesEveryTermAsItWasWrittenToItTest()
    {
        // The pairs of an answer are held to the terms asked by how they read: the property
        // that lets the host refuse any other pair. Generated terms of every theory are held to
        // it by the differential tests.
        Assert.Multiple(() =>
        {
            foreach (var (text, terms) in SPELLINGS)
            {
                var problem = SmtSolverProblems.Check("echo", text, terms);
                var solved = SmtSolverRig.Solve(problem, new SmtOptionsData());
                Assert.That(solved.Words(), Is.EqualTo("ok sat"), $"{text}: {solved}");
                if (solved.Values.Count != 1)
                    continue;

                var pairs = SmtReader.Read(solved.Values[0], SmtReaderLimits.Unbounded).Single().Items;
                Assert.That(pairs, Has.Count.EqualTo(terms.Length), text);
                for (var index = 0; index < Math.Min(pairs.Count, terms.Length); index++)
                {
                    var asked = SmtReader.Read(problem.Values[index], SmtReaderLimits.Unbounded).Single();
                    Assert.That(pairs[index].Items[0].Is(asked), Is.True, $"asked {asked}, echoed {pairs[index].Items[0]}");
                }
            }
        });
    }

    [Test]
    public void TheSolverEchoesTermsAtTheEdgesOfWhatARowMayAskTest()
    {
        // The longest term a row may ask, a deep one, names of every kind of character, and as
        // many terms as a row may hold: each comes back as it was written, and the row is
        // validated with all of them.
        const string text = "(set-logic QF_LIA)(declare-const x Int)(declare-const x!1 Int)(declare-const a.b Int)(declare-const $c Int)(declare-const d@e Int)(declare-const |x y| Int)(assert (= x 2))";
        var terms = new List<string>
        {
            $"(+ {string.Join(" ", Enumerable.Repeat("x", 30_000))})",
            string.Concat(Enumerable.Repeat("(- ", 2_000)) + "x" + new string(')', 2_000),
            "(+ x!1 a.b $c d@e |x y|)"
        };
        terms.AddRange(Enumerable.Range(0, SmtProblemSetLimits.MAX_VALUES - terms.Count).Select(index => $"(+ x {index})"));
        var problem = SmtSolverProblems.Check("edges", text, [.. terms]);

        var row = SmtNodeRig.Row(problem, new SmtOptionsData());
        Assert.That(SmtNodeRig.Words(row), Is.EqualTo("ok sat"));

        var pairs = SmtReader.Read(row.Values.Single(), SmtReaderLimits.Unbounded).Single().Items;
        Assert.That(pairs, Has.Count.EqualTo(SmtProblemSetLimits.MAX_VALUES));
        Assert.Multiple(() =>
        {
            for (var index = 0; index < pairs.Count; index++)
                Assert.That(pairs[index].Items[0].Is(SmtReader.Read(problem.Values[index], SmtReaderLimits.Unbounded).Single()), Is.True, $"term {index}");

            Assert.That(pairs[0].Items[1].Text, Is.EqualTo("60000"));
            Assert.That(SmtHostChecks.Check(problem, row), Is.EqualTo((SmtRunWords.VALIDATED, "", "")));
        });
    }

    #endregion

    #region Model Tests

    [TestCaseSource(nameof(UNUSED))]
    public void TheSolversModelDefinesEverySymbolTheProblemDeclaresTest(string text)
    {
        // Mentioned by an assertion or not: the solver completes its model when it prints it.
        // So no value of an honest row gives a constant the value its model leaves out; the
        // rule that lets a value do so is for an answer of values alone, and for a solver that
        // prints less. A model that leaves a symbol out shows here.
        var problem = SmtSolverProblems.Check("unused", text);
        var solved = SmtSolverRig.Solve(problem, new SmtOptionsData());
        Assert.That(solved.Words(), Is.EqualTo("ok sat"), solved.ToString());

        var validator = new SmtModelValidator(SmtSanitizer.Sanitize(text).Problem!);
        var validation = validator.Validate(SmtModel.Read(solved.Models[0], null, SmtReaderLimits.Unbounded), SmtEvaluationBudget.Default);

        Assert.That(validator.Declared, Is.GreaterThan(1));
        Assert.That(validation.Defined, Is.EqualTo(validator.Declared), solved.Models[0]);
        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), validation.ToString());
    }

    #endregion

    #region Row Tests

    [TestCaseSource(nameof(ComputedNames))]
    public void HonestRowIsValidatedWithTheValuesItWasAskedForTest(string name)
    {
        var (_, text, terms) = COMPUTED.Single(one => one.Name == name);
        var problem = SmtSolverProblems.Check(name, text, terms);

        var row = SmtNodeRig.Row(problem, new SmtOptionsData());
        var alone = SmtSolverProblems.Validate(text, row.Models.FirstOrDefault());
        var check = SmtHostChecks.Check(problem, row);

        TestContext.Out.WriteLine(row.Values.FirstOrDefault());
        Assert.That(SmtNodeRig.Words(row), Is.EqualTo("ok sat"));
        Assert.That(alone.Class, Is.EqualTo(SmtValidationClass.Validated), "the model alone, as the row was classed before its values were held to it");
        Assert.That(check, Is.EqualTo((SmtRunWords.VALIDATED, "", "")), row.Values.FirstOrDefault());
    }

    [TestCaseSource(nameof(NotComputedNames))]
    public void HonestRowWithAValueTheEvaluatorDoesNotComputeIsPartialAndNeverFailedTest(string name)
    {
        var (_, text, term, reason) = NOT_COMPUTED.Single(one => one.Name == name);
        var problem = SmtSolverProblems.Check(name, text, term);

        var row = SmtNodeRig.Row(problem, new SmtOptionsData());
        var check = SmtHostChecks.Check(problem, row);

        TestContext.Out.WriteLine(row.Values.FirstOrDefault());
        Assert.That(SmtNodeRig.Words(row), Is.EqualTo("ok sat"));
        Assert.That(check, Is.EqualTo((SmtRunWords.PARTIAL, reason, "")), row.Values.FirstOrDefault());
    }

    [Test]
    public void HonestEnumerationIsValidatedWithTheValuesOfEveryModelTest()
    {
        // Four queens have two placements, and z one of two values beside each.
        var text = SmtSolverProblems.Queens(4) + "(declare-const z Int)(assert (<= 0 z 1))";
        var problem = SmtSolverProblems.Enumerate("queens", text, 10, [.. SmtSolverProblems.QueenNames(4), "z"]);

        var row = SmtNodeRig.Row(problem, new SmtOptionsData());
        var check = SmtHostChecks.Check(problem, row);

        Assert.That(SmtNodeRig.Words(row), Is.EqualTo("ok sat 4 complete"));
        Assert.That(check, Is.EqualTo((SmtRunWords.VALIDATED, "", "")));
    }

    [Test]
    public void RowOfTheSolverWithOneValueChangedIsFailedTest()
    {
        // What a node that solved honestly and then wrote another value gets: the same model, the same terms.
        var problem = SmtSolverProblems.Check("forged", SmtSolverCases.ONE_SOLUTION, "x", "(- x y)", "(* 2 y)");
        var row = SmtNodeRig.Row(problem, new SmtOptionsData());
        Assert.That(SmtHostChecks.Check(problem, row), Is.EqualTo((SmtRunWords.VALIDATED, "", "")));
        Assert.That(row.Values, Is.EqualTo(new[] { "((x 7)\n ((- x y) 4)\n ((* 2 y) 6))\n" }));

        row.Values = ["((x 7)\n ((- x y) 4)\n ((* 2 y) 7))\n"];
        var forged = SmtHostChecks.Check(problem, row);

        row.Values = ["((x 7)\n ((- y x) 4)\n ((* 2 y) 6))\n"];
        var renamed = SmtHostChecks.Check(problem, row);

        Assert.That(forged, Is.EqualTo((SmtRunWords.FAILED, SmtValidationReasons.VALUE_MISMATCH, "3")));
        Assert.That(renamed, Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.UNREADABLE_VALUES, "")));
    }

    #endregion

    #region Tools

    private static IEnumerable<string> ComputedNames()
    {
        return COMPUTED.Select(one => one.Name);
    }

    private static IEnumerable<string> NotComputedNames()
    {
        return NOT_COMPUTED.Select(one => one.Name);
    }

    #endregion
}
