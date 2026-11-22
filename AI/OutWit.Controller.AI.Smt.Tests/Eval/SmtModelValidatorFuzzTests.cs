using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// The evaluator on input nobody vouches for: the fixtures' problems and models damaged at
/// random, from a fixed seed. Whatever the damage, a validation ends with one of the three
/// classes and a reason of the list, within its budget; it never throws, and it never falls
/// back on the reason that stands for a defect of the evaluator itself.
/// </summary>
[TestFixture]
public class SmtModelValidatorFuzzTests
{
    #region Constants

    private const int SEED = 20261122;
    private const int ROUNDS = 6000;
    private const int MAX_DAMAGES = 3;

    private static readonly SmtEvaluationBudget BUDGET = new() { MaxSteps = 200_000, MaxDepth = 2_000 };

    #endregion

    #region Fuzz Tests

    [Test]
    public void DamagedProblemsAndModelsAlwaysEndWithAClassAndAKnownReasonTest()
    {
        var fuzzer = new SmtFuzzer(SEED);
        var fixtures = SmtFixtures.All.Select(fixture => (
            Commands: SmtCheck.Commands(fixture.Problem),
            Model: Items(fixture.Model),
            Values: Items(fixture.Values))).ToList();
        var classes = new Dictionary<SmtValidationClass, int>();
        var reasons = new Dictionary<string, int>();

        for (var round = 0; round < ROUNDS; round++)
        {
            var (commands, model, values) = fixtures[round % fixtures.Count];

            // Most rounds change what a command or an item of the model says and leave its
            // shape; one in five changes any place at all, shapes included.
            var target = fuzzer.Next(10);
            for (var damage = fuzzer.Next(MAX_DAMAGES) + 1; damage > 0; damage--)
            {
                switch (target)
                {
                    case < 5:
                        commands = fuzzer.DamageInside(commands);
                        break;
                    case < 8:
                        model = model == null ? null : fuzzer.DamageInside(model);
                        values = values == null ? null : fuzzer.DamageInside(values);
                        break;
                    case 8:
                        commands = fuzzer.Damage(commands);
                        break;
                    default:
                        model = model == null ? null : fuzzer.Damage(model);
                        values = values == null ? null : fuzzer.Damage(values);
                        break;
                }
            }

            var validation = Validate(commands, Text(model, fuzzer), Text(values, fuzzer));

            AssertWellFormed(validation, round);
            classes[validation.Class] = classes.GetValueOrDefault(validation.Class) + 1;
            reasons[validation.Reason ?? ""] = reasons.GetValueOrDefault(validation.Reason ?? "") + 1;
        }

        // The damage is light enough that all three classes come up, and heavy enough that
        // the evaluator is driven through most of its refusals.
        TestContext.Out.WriteLine(string.Join(", ", classes.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key} {pair.Value}")));
        TestContext.Out.WriteLine(string.Join(", ", reasons.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value}")));
        Assert.That(classes.GetValueOrDefault(SmtValidationClass.Validated), Is.GreaterThan(ROUNDS / 50));
        Assert.That(classes.GetValueOrDefault(SmtValidationClass.Failed), Is.GreaterThan(ROUNDS / 50));
        Assert.That(classes.GetValueOrDefault(SmtValidationClass.Partial), Is.GreaterThan(ROUNDS / 4));
        Assert.That(reasons.Keys, Has.Count.GreaterThanOrEqualTo(14));
        Assert.That(reasons.Keys, Has.None.EqualTo(SmtValidationReasons.EVALUATOR_ERROR));
    }

    [Test]
    public void ModelTextCutAnywhereIsReadOrUnreadableTest()
    {
        var fuzzer = new SmtFuzzer(SEED + 1);
        foreach (var fixture in SmtFixtures.All)
        {
            var validator = new SmtModelValidator(SmtCheck.Commands(fixture.Problem));
            for (var round = 0; round < 40; round++)
            {
                var model = Cut(fixture.Model, fuzzer);
                var values = Cut(fixture.Values, fuzzer);

                var validation = validator.Validate(SmtModel.Read(model, values, SmtReaderLimits.Problem), BUDGET);

                AssertWellFormed(validation, round);
            }
        }
    }

    [Test]
    public void EveryFixtureEndsWithAKnownReasonUnderEveryBudgetTest()
    {
        // A budget that ends the evaluation anywhere: at each step count from nothing up.
        foreach (var fixture in SmtFixtures.All)
        {
            var validator = new SmtModelValidator(SmtCheck.Commands(fixture.Problem));
            var model = SmtCheck.Model(fixture.Model, fixture.Values);
            var needed = validator.Validate(model, SmtEvaluationBudget.Default).Steps;

            for (long steps = 0; steps <= needed; steps += Math.Max(1, needed / 60))
            {
                var validation = validator.Validate(model, new SmtEvaluationBudget { MaxSteps = steps });

                AssertWellFormed(validation, (int)steps);
                Assert.That(validation.Class, steps < needed ? Is.EqualTo(SmtValidationClass.Partial) : Is.EqualTo(SmtValidationClass.Validated), $"{fixture.Name} at {steps} of {needed}");
            }

            for (var depth = 1; depth <= 12; depth++)
                AssertWellFormed(validator.Validate(model, new SmtEvaluationBudget { MaxDepth = depth }), depth);
        }
    }

    #endregion

    #region Tools

    private static SmtValidation Validate(IReadOnlyList<SmtExpression> commands, string? model, string? values)
    {
        return new SmtModelValidator(commands).Validate(SmtModel.Read(model, values, SmtReaderLimits.Problem), BUDGET);
    }

    private static void AssertWellFormed(SmtValidation validation, int round)
    {
        var context = $"round {round}: {validation}";
        Assert.That(validation.Steps, Is.InRange(0, SmtEvaluationBudget.DEFAULT_STEPS), context);
        Assert.That(validation.True, Is.InRange(0, validation.Assertions), context);
        switch (validation.Class)
        {
            case SmtValidationClass.Validated:
                Assert.That(validation.Reason, Is.Null, context);
                Assert.That(validation.True, Is.EqualTo(validation.Assertions), context);
                break;
            case SmtValidationClass.Failed:
                Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.ASSERTION_FALSE), context);
                Assert.That(int.Parse(validation.Detail!), Is.InRange(1, validation.Assertions), context);
                break;
            case SmtValidationClass.Partial:
                Assert.That(SmtValidationReasons.All, Does.Contain(validation.Reason), context);
                Assert.That(validation.Reason, Is.Not.EqualTo(SmtValidationReasons.EVALUATOR_ERROR).And.Not.EqualTo(SmtValidationReasons.ASSERTION_FALSE), context);
                break;
            default:
                Assert.Fail(context);
                break;
        }
    }

    private static IReadOnlyList<SmtExpression>? Items(string? text)
    {
        return text == null ? null : SmtReader.Read(text, SmtReaderLimits.Unbounded).Single().Items;
    }

    /// <summary>
    /// The damaged answer as the text a solver would have printed, now and then cut short.
    /// </summary>
    private static string? Text(IReadOnlyList<SmtExpression>? items, SmtFuzzer fuzzer)
    {
        if (items == null)
            return null;

        var text = SmtExpression.List(items).ToString();
        return fuzzer.Next(10) == 0 ? text[..fuzzer.Next(text.Length + 1)] : text;
    }

    private static string? Cut(string? text, SmtFuzzer fuzzer)
    {
        return text == null ? null : text[..fuzzer.Next(text.Length + 1)];
    }

    #endregion
}
