using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

[TestFixture]
public class SmtModelValidatorFixtureTests
{
    #region Constants

    private static readonly string[] LOGICS =
    [
        "QF_LIA", "QF_LRA", "QF_BV", "QF_UF", "QF_AX", "QF_ALIA", "QF_UFLIA", "QF_DT", "QF_UFDT", "QF_S"
    ];

    #endregion

    #region Validated Tests

    [TestCaseSource(nameof(FixtureNames))]
    public void ValidateFindsEveryAssertionOfAFixtureTrueTest(string name)
    {
        var fixture = Fixture(name);

        var validation = SmtCheck.Validate(fixture.Problem, fixture.Model, fixture.Values);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), $"{name}: {validation}");
        Assert.That(validation.Reason, Is.Null);
        Assert.That(validation.Detail, Is.Null);
        Assert.That(validation.True, Is.EqualTo(validation.Assertions));
        Assert.That(validation.Assertions, Is.GreaterThan(3));
        Assert.That(validation.Steps, Is.GreaterThan(0));
    }

    [TestCaseSource(nameof(NamesOfFixturesWithBothAnswers))]
    public void ValidateTakesTheAnswerOfGetModelAloneTest(string name)
    {
        // Where a fixture carries both answers, the answer of get-model alone is a model too:
        // it names everything the problem declares.
        var fixture = Fixture(name);

        var byModel = SmtCheck.Validate(fixture.Problem, fixture.Model, null);

        Assert.That(byModel.Class, Is.EqualTo(SmtValidationClass.Validated), $"{name}: {byModel}");
        Assert.That(byModel.Defined, Is.EqualTo(SmtCheck.Validate(fixture.Problem, fixture.Model, fixture.Values).Defined));
    }

    [Test]
    public void FixturesCoverEveryLogicOfTheListTest()
    {
        Assert.That(SmtFixtures.All.Select(fixture => fixture.Logic).Distinct(), Is.SupersetOf(LOGICS));
        Assert.That(SmtFixtures.All.Select(fixture => fixture.Name), Is.Unique);
    }

    [TestCaseSource(nameof(FixtureNames))]
    public void ValidateCountsTheSymbolsTheModelDefinesTest(string name)
    {
        var fixture = Fixture(name);
        var validator = new SmtModelValidator(SmtCheck.Commands(fixture.Problem));

        var validation = validator.Validate(SmtCheck.Model(fixture.Model, fixture.Values), SmtEvaluationBudget.Default);

        Assert.That(validation.Defined, Is.EqualTo(validator.Declared), name);
        Assert.That(validation.Assertions, Is.EqualTo(validator.Assertions));
    }

    #endregion

    #region Mutant Tests

    [TestCaseSource(nameof(FixtureNames))]
    public void ValidateDecidesEveryMutantOfAFixtureTest(string name)
    {
        var fixture = Fixture(name);
        var mutants = new SmtModelMutants(fixture).Make();

        Assert.That(mutants, Is.Not.Empty, name);
        foreach (var (symbol, model, values) in mutants)
        {
            var validation = SmtCheck.Validate(fixture.Problem, model, values);

            // One changed value: the model no longer satisfies the problem, or the symbol was free to change.
            Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Failed).Or.EqualTo(SmtValidationClass.Validated), $"{name}, {symbol}: {validation}");
            if (validation.Class == SmtValidationClass.Failed)
            {
                Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.ASSERTION_FALSE));
                Assert.That(int.Parse(validation.Detail!), Is.InRange(1, validation.Assertions));
                Assert.That(validation.True, Is.LessThan(validation.Assertions));
            }
        }
    }

    [Test]
    public void ValidateFailsNearlyEveryMutantTest()
    {
        var failed = 0;
        var kept = new List<string>();
        foreach (var fixture in SmtFixtures.All)
        {
            foreach (var (symbol, model, values) in new SmtModelMutants(fixture).Make())
            {
                if (SmtCheck.Validate(fixture.Problem, model, values).Class == SmtValidationClass.Failed)
                    failed++;
                else
                    kept.Add($"{fixture.Name}/{symbol}");
            }
        }

        // The fixtures pin their symbols down: no mutant is expected to survive.
        Assert.That(kept, Is.Empty);
        Assert.That(failed, Is.GreaterThan(40));
    }

    #endregion

    #region Tools

    private static IEnumerable<string> FixtureNames()
    {
        return SmtFixtures.All.Select(fixture => fixture.Name);
    }

    private static IEnumerable<string> NamesOfFixturesWithBothAnswers()
    {
        return SmtFixtures.All.Where(fixture => fixture.Model != null && fixture.Values != null).Select(fixture => fixture.Name);
    }

    private static SmtFixture Fixture(string name)
    {
        return SmtFixtures.All.Single(fixture => fixture.Name == name);
    }

    #endregion
}
