using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// The rule of <see cref="SmtRefusalCodes.BUILTIN_NAME"/>: under <c>ALL</c>, or with no logic
/// set, a function with parameters or a constructor with fields named after one of the
/// solver's transcendental operators is refused, in every command that declares one; a
/// constant, a selector, a constructor without fields, another name, and every declaration
/// under another logic are taken. What the solver itself does with each of these is asked of it
/// in <c>SmtSolverBuiltinNameTests</c>.
/// </summary>
[TestFixture]
public class SmtSanitizerBuiltinTests
{
    #region Constants

    private static readonly string[] OVERRIDING_LOGICS = ["", "(set-logic ALL)\n"];

    #endregion

    #region Refusal Tests

    [Test]
    public void EveryNameIsRefusedAsAFunctionUnderAllAndWithoutALogicTest()
    {
        Assert.That(SmtBuiltinNames.OVERRIDING, Has.Count.EqualTo(17));
        foreach (var logic in OVERRIDING_LOGICS)
        {
            foreach (var name in SmtBuiltinNames.OVERRIDING)
            {
                var refusal = SmtSanitizerChecks.Refuse($"{logic}(declare-fun {name} (Real) Real)", SmtRefusalCodes.BUILTIN_NAME);

                Assert.That(refusal.Command, Is.EqualTo("declare-fun"), name);
                Assert.That(refusal.Line, Is.EqualTo(logic.Length == 0 ? 1 : 2), name);
                Assert.That(refusal.Reason, Does.StartWith($"'{name}' is an operator the solver has built in"), name);
            }
        }
    }

    [TestCase("(declare-fun sin (Real) Real)", "declare-fun")]
    [TestCase("(declare-fun |cos| (Real Int) Bool)", "declare-fun")]
    [TestCase("(define-fun exp ((x Real)) Real x)", "define-fun")]
    [TestCase("(define-fun-rec log ((x Real)) Real (log x))", "define-fun-rec")]
    [TestCase("(define-funs-rec ((f ((n Int)) Int) (tanh ((x Real)) Real)) (n x))", "define-funs-rec")]
    [TestCase("(declare-datatype Op ((id) (atan2 (y Real) (x Real))))", "declare-datatype")]
    [TestCase("(declare-datatype Box (par (T) ((ln (content T)))))", "declare-datatype")]
    [TestCase("(declare-datatypes ((A 0) (B 0)) (((a)) ((b) (arctan2 (v Real)))))", "declare-datatypes")]
    [TestCase("(declare-datatypes ((P 1)) ((par (T) ((none) (sinh (value T))))))", "declare-datatypes")]
    public void EveryPositionThatDeclaresAFunctionIsRefusedTest(string command, string head)
    {
        foreach (var logic in OVERRIDING_LOGICS)
            Assert.That(SmtSanitizerChecks.Refuse(logic + command, SmtRefusalCodes.BUILTIN_NAME).Command, Is.EqualTo(head));
    }

    [Test]
    public void ADeclarationBeforeTheLogicIsUnderNoLogicTest()
    {
        // The solver reads a declaration with the logic set so far: none.
        SmtSanitizerChecks.Refuse("(declare-fun sin (Real) Real)\n(set-logic QF_UFNRA)", SmtRefusalCodes.BUILTIN_NAME);
    }

    [Test]
    public void TheNameIsTheOneRefusedAndNotTheTextTest()
    {
        var refusal = SmtSanitizerChecks.Refuse("(set-logic ALL)\n(declare-datatypes ((A 0))\n(((mk (x Real))\n(acosh (y Real)))))", SmtRefusalCodes.BUILTIN_NAME);

        Assert.That(refusal.Line, Is.EqualTo(4), "the line of the name");
        Assert.That(refusal.Reason, Does.Contain("'acosh'"));
        Assert.That(refusal.Reason, Does.Not.Contain("mk"));
    }

    [Test]
    public void AnotherRuleOfTheSameCommandComesFirstTest()
    {
        // The shape and the tokens are looked at before the names: a malformed command is malformed.
        SmtSanitizerChecks.Refuse("(declare-fun sin (Real))", SmtRefusalCodes.COMMAND_MALFORMED);
        SmtSanitizerChecks.Refuse("(declare-fun sin (Real) oc!r)", SmtRefusalCodes.RESERVED_PREFIX);
    }

    #endregion

    #region Accepted Tests

    [TestCase("(declare-fun sin () Real)")]
    [TestCase("(declare-const exp Real)")]
    [TestCase("(define-fun pi () Real 3.0)")]
    [TestCase("(declare-fun pi () Real)")]
    [TestCase("(declare-datatype Wave ((sin) (cos)))")]
    [TestCase("(declare-datatype Wave ((mk (sin Real) (cos Real))))")]
    [TestCase("(declare-fun sine (Real) Real)")]
    [TestCase("(declare-fun bvredor ((_ BitVec 8)) (_ BitVec 1))")]
    [TestCase("(declare-fun abs (Int) Int)")]
    [TestCase("(declare-fun fp.i6 () (_ BitVec 29))")]
    [TestCase("(declare-fun SIN (Real) Real)")]
    public void WhatTheSolverDoesNotReadTwoWaysIsTakenUnderAllTest(string command)
    {
        foreach (var logic in OVERRIDING_LOGICS)
            SmtSanitizerChecks.HoldProperties(SmtSanitizerChecks.Accept(logic + command));
    }

    [TestCase("(set-logic QF_UFNRA)")]
    [TestCase("(set-logic UFNIRA)")]
    [TestCase("(set-logic QF_UFLRA)")]
    [TestCase("(set-logic AUFNIRA)")]
    [TestCase("(set-logic all)")]
    [TestCase("(set-logic |ALL |)")]
    public void EveryFunctionIsTakenUnderAnotherLogicTest(string logic)
    {
        foreach (var name in SmtBuiltinNames.OVERRIDING)
        {
            SmtSanitizerChecks.Accept($"{logic}(declare-fun {name} (Real) Real)(define-fun-rec {name}2 ((x Real)) Real x)");
            SmtSanitizerChecks.Accept($"{logic}(define-fun {name} ((x Real)) Real x)");
            SmtSanitizerChecks.Accept($"{logic}(declare-datatype D (({name} (v Real))))");
        }
    }

    [Test]
    public void TheLogicIsTheFirstOneSetTest()
    {
        // A second set-logic is the solver's error; the sanitizer keeps reading under the first.
        SmtSanitizerChecks.Accept("(set-logic QF_UFNRA)(set-logic ALL)(declare-fun sin (Real) Real)");
        SmtSanitizerChecks.Refuse("(set-logic ALL)(set-logic QF_UFNRA)(declare-fun sin (Real) Real)", SmtRefusalCodes.BUILTIN_NAME);
    }

    #endregion
}
