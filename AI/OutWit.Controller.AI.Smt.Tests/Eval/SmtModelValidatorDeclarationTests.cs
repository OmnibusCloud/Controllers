using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// The declarations a problem may make, in the forms solvers take, and the default value each
/// sort gives a symbol the model leaves out.
/// </summary>
[TestFixture]
public class SmtModelValidatorDeclarationTests
{
    #region Information Tests

    [Test]
    public void WhatAProblemSaysAboutItselfMeansNothingToTheEvaluationTest()
    {
        // set-info with any content, characters outside ASCII included, and set-logic are
        // passed over: nothing in them is read as a term or a name.
        var accent = (char)0xE9;
        var problem = $"(set-info :source |a problem by Ren{accent}, with (parentheses) and \"quotes\"|)"
                      + $"(set-info :note \"caf{accent} (! x :named n)\")(set-info :status sat)(set-info)(set-info :x (! true :named x) (declare-const x Bool))"
                      + "(set-logic QF_LIA)(set-logic)(declare-const x Int)(assert (! (> x 0) :named n))(assert n)";

        var validation = SmtCheck.Validate(problem, null, "((x 1))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), validation.ToString());
        Assert.That(validation.Assertions, Is.EqualTo(2));
        Assert.That(validation.Defined, Is.EqualTo(1));
    }

    #endregion

    #region Sort Tests

    [Test]
    public void SortAliasStandsForItsSortTest()
    {
        const string problem = "(define-sort Word () (_ BitVec 8))(define-sort Map (K V) (Array K V))(define-sort Table () (Map Word Int))"
                               + "(declare-const t Table)(declare-const w Word)(assert (= (select t w) 5))(assert (= (select t (bvadd w #x01)) 0))";
        const string model = "((define-fun t () (Array (_ BitVec 8) Int) (store ((as const (Array (_ BitVec 8) Int)) 0) #x10 5)) (define-fun w () (_ BitVec 8) #x10))";

        Assert.That(SmtCheck.Validate(problem, model).Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    [Test]
    public void DeclaredSortWithArgumentsIsASortForEachOfThemTest()
    {
        const string problem = "(declare-sort Box 1)(declare-const a (Box Int))(declare-const b (Box Int))(declare-const c (Box Bool))(assert (distinct a b))(assert (= c c))";
        const string model = "((declare-fun Box!val!0 () (Box Int)) (declare-fun Box!val!1 () (Box Int)) (declare-fun Box!val!2 () (Box Bool))"
                             + " (define-fun a () (Box Int) Box!val!0) (define-fun b () (Box Int) Box!val!1) (define-fun c () (Box Bool) Box!val!2))";
        var crossed = model.Replace("(define-fun c () (Box Bool) Box!val!2)", "(define-fun c () (Box Bool) Box!val!0)");

        Assert.That(SmtCheck.Validate(problem, model).Class, Is.EqualTo(SmtValidationClass.Validated));
        Assert.That(SmtCheck.Validate(problem, crossed).Reason, Is.EqualTo(SmtValidationReasons.ILL_FORMED_TERM));
    }

    [Test]
    public void DeclareSortWithoutAnArityDeclaresASortWithoutArgumentsTest()
    {
        const string problem = "(declare-sort U)(declare-const u U)(assert (= u u))";

        Assert.That(SmtCheck.Validate(problem, "((declare-fun U!val!0 () U) (define-fun u () U U!val!0))").Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    #endregion

    #region Datatype Tests

    [TestCase("(declare-datatypes ((Opt 1)) ((par (T) ((none) (some (val T))))))")]
    [TestCase("(declare-datatype Opt (par (T) ((none) (some (val T)))))")]
    [TestCase("(declare-datatypes (T) ((Opt none (some (val T)))))")]
    public void DatatypeWithParametersIsDeclaredInEveryFormTest(string declaration)
    {
        var problem = declaration + "(declare-const o (Opt Int))(declare-const p (Opt (Opt Bool)))(assert (= (val o) 3))(assert ((_ is some) p))(assert (= (val p) (as none (Opt Bool))))(assert (not (is-none o)))";

        var validation = SmtCheck.Validate(problem, null, "((o (some 3)) (p (some none)))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), validation.ToString());
    }

    [Test]
    public void DatatypesOfOneCommandUseEachOtherTest()
    {
        const string problem = "(declare-datatypes ((Expr 0) (Args 0)) (((num (value Int)) (app (args Args))) ((end) (arg (head Expr) (rest Args)))))"
                               + "(define-fun-rec size ((e Expr)) Int (match e (((num v) 1) ((app a) (+ 1 (sizes a))))))"
                               + "(define-fun-rec sizes ((a Args)) Int (match a ((end 0) ((arg h r) (+ (size h) (sizes r))))))"
                               + "(declare-const e Expr)(assert (= (size e) 4))(assert ((_ is app) e))";

        var validation = SmtCheck.Validate(problem, null, "((e (app (arg (num 1) (arg (app (arg (num 2) end)) end)))))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), validation.ToString());
    }

    [Test]
    public void FunctionOverDatatypesIsAppliedByValueTest()
    {
        const string problem = "(declare-datatype Point ((pt (px Int) (py Int))))(declare-fun weight (Point) Real)(declare-const p Point)"
                               + "(assert (= (weight p) 2.5))(assert (= (weight (pt (py p) (px p))) 0.0))(assert (distinct p (pt 2 1)))";
        const string model = "((define-fun p () Point (pt 1 2)) (define-fun weight ((x!0 Point)) Real (ite (= x!0 (pt 1 2)) (/ 5.0 2.0) 0.0)))";

        Assert.That(SmtCheck.Validate(problem, model).Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    #endregion

    #region Default Tests

    [TestCase("Bool", "(not x)")]
    [TestCase("Int", "(= x 0)")]
    [TestCase("Real", "(= x 0.0)")]
    [TestCase("String", "(= x \"\")")]
    [TestCase("(_ BitVec 12)", "(= x #x000)")]
    [TestCase("(Array Int Bool)", "(not (select x 7))")]
    [TestCase("(Array Int (Array Int Int))", "(= (select (select x 1) 2) 0)")]
    [TestCase("Color", "(= x red)")]
    [TestCase("L", "(= x nil)")]
    [TestCase("Tree", "(= x leaf)")]
    [TestCase("(Pair Int L)", "(= x (mk 0 nil))")]
    [TestCase("U", "(= x x)")]
    public void SymbolTheModelLeavesOutIsGivenTheDefaultOfItsSortTest(string sort, string assertion)
    {
        const string declarations = "(declare-sort U 0)(declare-datatype Color ((red) (green)))(declare-datatypes ((L 0)) (((cons (hd Int) (tl L)) (nil))))"
                                    + "(declare-datatype Tree ((node (left Tree) (label Int) (right Tree)) (leaf)))(declare-datatypes ((Pair 2)) ((par (A B) ((mk (fst A) (snd B))))))";

        var validation = SmtCheck.Validate($"{declarations}(declare-const x {sort})(assert {assertion})", null);

        // True with the default: the model, completed, is a witness.
        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), validation.ToString());
        Assert.That(validation.Defined, Is.EqualTo(0));
    }

    [Test]
    public void SymbolOfASortTheModelPopulatesIsGivenItsFirstElementTest()
    {
        const string problem = "(declare-sort U 0)(declare-const u U)(declare-const v U)(declare-const w U)(assert (= u v))(assert (not (= u w)))";

        var validation = SmtCheck.Validate(problem, "((declare-fun U!val!0 () U) (declare-fun U!val!1 () U) (define-fun w () U U!val!1))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated));
        Assert.That(validation.Defined, Is.EqualTo(1));
    }

    #endregion
}
