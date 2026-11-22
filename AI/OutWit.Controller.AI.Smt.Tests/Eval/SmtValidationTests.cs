using System.Reflection;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

[TestFixture]
public class SmtValidationTests
{
    #region Validation Tests

    [Test]
    public void IsComparesEveryPartOfAnOutcomeTest()
    {
        var validation = Sample();

        Assert.That(validation.Is(validation.Clone()), Is.True);
        Assert.That(validation.Is(new SmtEvaluationBudget()), Is.False);
        Assert.That(validation.Is(Changed(validation, nameof(SmtValidation.Class), SmtValidationClass.Partial)), Is.False);
        Assert.That(validation.Is(Changed(validation, nameof(SmtValidation.Reason), SmtValidationReasons.QUANTIFIER)), Is.False);
        Assert.That(validation.Is(Changed(validation, nameof(SmtValidation.Reason), null)), Is.False);
        Assert.That(validation.Is(Changed(validation, nameof(SmtValidation.Detail), "4")), Is.False);
        Assert.That(validation.Is(Changed(validation, nameof(SmtValidation.Assertions), 6)), Is.False);
        Assert.That(validation.Is(Changed(validation, nameof(SmtValidation.True), 1)), Is.False);
        Assert.That(validation.Is(Changed(validation, nameof(SmtValidation.Defined), 8)), Is.False);
        Assert.That(validation.Is(Changed(validation, nameof(SmtValidation.Steps), 43L)), Is.False);
    }

    [Test]
    public void CloneCopiesEveryPartOfAnOutcomeTest()
    {
        var validation = Sample();

        var clone = validation.Clone();

        Assert.That(clone, Is.Not.SameAs(validation));
        Assert.That(clone.Class, Is.EqualTo(SmtValidationClass.Failed));
        Assert.That(clone.Reason, Is.EqualTo(SmtValidationReasons.ASSERTION_FALSE));
        Assert.That(clone.Detail, Is.EqualTo("3"));
        Assert.That(clone.Assertions, Is.EqualTo(5));
        Assert.That(clone.True, Is.EqualTo(2));
        Assert.That(clone.Defined, Is.EqualTo(7));
        Assert.That(clone.Steps, Is.EqualTo(42));
    }

    [Test]
    public void ToStringNamesTheClassAndTheReasonTest()
    {
        Assert.That(Sample().ToString(), Does.Contain("Failed").And.Contain(SmtValidationReasons.ASSERTION_FALSE).And.Contain("42"));
    }

    [Test]
    public void ValidationCarriesNoTextOfTheProblemOrOfTheModelTest()
    {
        // The names and the string are the stranger's; nothing of them may come back.
        const string problem = "(declare-const secret_name Int)(declare-fun hidden_function (Int) Int)(assert (= (hidden_function secret_name) (str.len \"private text\")))(assert (unknown_operator secret_name))";
        const string model = "((define-fun secret_name () Int 12) (define-fun hidden_function ((x!0 Int)) Int 0))";

        foreach (var validation in new[] { SmtCheck.Validate(problem, model), SmtCheck.Validate(problem, "(private text)"), SmtCheck.Validate(problem + "(private command)", model) })
        {
            var text = validation.ToString() + validation.Reason + validation.Detail;

            Assert.That(text, Does.Not.Contain("secret").And.Not.Contain("hidden").And.Not.Contain("private").And.Not.Contain("unknown_operator"));
        }
    }

    #endregion

    #region Reason Tests

    [Test]
    public void ReasonsAreFixedCodesTest()
    {
        var constants = typeof(SmtValidationReasons).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!).ToList();

        Assert.That(SmtValidationReasons.All, Is.EquivalentTo(constants));
        Assert.That(SmtValidationReasons.All, Is.Unique);
        Assert.That(SmtValidationReasons.All, Has.All.Match("^[a-z_]+$"));
    }

    [Test]
    public void ClassesKeepTheirNumbersTest()
    {
        // The numbers go into result documents.
        Assert.That((int)SmtValidationClass.Validated, Is.EqualTo(0));
        Assert.That((int)SmtValidationClass.Partial, Is.EqualTo(1));
        Assert.That((int)SmtValidationClass.Failed, Is.EqualTo(2));
    }

    #endregion

    #region Budget Tests

    [Test]
    public void DefaultBudgetHasTheNumbersOfItsConstantsTest()
    {
        var budget = SmtEvaluationBudget.Default;

        Assert.That(budget.MaxSteps, Is.EqualTo(SmtEvaluationBudget.DEFAULT_STEPS));
        Assert.That(budget.MaxDepth, Is.EqualTo(SmtEvaluationBudget.DEFAULT_DEPTH));
        Assert.That(budget.MaxIntegerBits, Is.EqualTo(SmtEvaluationBudget.DEFAULT_INTEGER_BITS));
        Assert.That(budget.MaxBitVectorBits, Is.EqualTo(SmtEvaluationBudget.DEFAULT_BIT_VECTOR_BITS));
        Assert.That(budget.MaxStringCharacters, Is.EqualTo(SmtEvaluationBudget.DEFAULT_STRING_CHARACTERS));
        Assert.That(budget.MaxArrayEntries, Is.EqualTo(SmtEvaluationBudget.DEFAULT_ARRAY_ENTRIES));
        Assert.That(budget.MaxDepth, Is.LessThanOrEqualTo(SmtEvaluationBudget.DEPTH_CEILING));
        Assert.That(SmtEvaluationBudget.DEPTH_CEILING, Is.GreaterThan(SmtReaderLimits.PROBLEM_DEPTH));
    }

    [Test]
    public void BudgetIsComparedAndCopiedByItsNumbersTest()
    {
        var budget = new SmtEvaluationBudget { MaxSteps = 1, MaxDepth = 2, MaxIntegerBits = 3, MaxBitVectorBits = 4, MaxStringCharacters = 5, MaxArrayEntries = 6 };

        Assert.That(budget.Is(budget.Clone()), Is.True);
        Assert.That(budget.Clone(), Is.Not.SameAs(budget));
        Assert.That(budget.Is(SmtEvaluationBudget.Default), Is.False);
        Assert.That(budget.Is(Sample()), Is.False);
        foreach (var property in typeof(SmtEvaluationBudget).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.CanWrite))
        {
            var changed = budget.Clone();
            property.SetValue(changed, Convert.ChangeType(99, property.PropertyType));

            Assert.That(budget.Is(changed), Is.False, property.Name);
        }
    }

    [Test]
    public void BudgetOfNoSenseIsTakenAsTheNearestOneThatHasSomeTest()
    {
        var budget = new SmtEvaluationBudget { MaxSteps = -5, MaxDepth = -5, MaxIntegerBits = -5, MaxBitVectorBits = -5, MaxStringCharacters = -5, MaxArrayEntries = -5 };

        var validation = SmtCheck.Validate("(assert true)", null, null, budget);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.BUDGET_STEPS));
        Assert.That(validation.Steps, Is.EqualTo(0));
    }

    #endregion

    #region Tools

    private static SmtValidation Sample()
    {
        return new SmtValidation
        {
            Class = SmtValidationClass.Failed,
            Reason = SmtValidationReasons.ASSERTION_FALSE,
            Detail = "3",
            Assertions = 5,
            True = 2,
            Defined = 7,
            Steps = 42
        };
    }

    private static SmtValidation Changed(SmtValidation validation, string property, object? value)
    {
        var changed = validation.Clone();
        typeof(SmtValidation).GetProperty(property)!.SetValue(changed, value);
        return changed;
    }

    #endregion
}
