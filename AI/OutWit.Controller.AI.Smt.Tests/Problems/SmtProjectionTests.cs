using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// The one check of a projection (<see cref="SmtProjection"/>), which the reader of a problem set
/// and a node's plan both make: what it gives when the names are taken, and the refusals only a
/// node can meet, since a row's own shape keeps them from a reader (no name at all, more names
/// than a row may hold, a name that is not there). The rules a row can break are held through
/// the reader (<see cref="SmtProblemSetQueryTests"/>, <see cref="SmtProblemSetProjectionTests"/>).
/// The sort of a constant is no rule of it: every case of <see cref="SmtProjectionCases"/> is
/// taken, also the ones over a sort the problem only declares.
/// </summary>
[TestFixture]
public sealed class SmtProjectionTests
{
    #region Constants

    private const string PROBLEM = "(declare-const x Int)(declare-fun |a b| () Bool)(declare-const y Int)(declare-fun f (Int) Int)";

    #endregion

    #region Taken Tests

    [Test]
    public void TheConstantsComeBackAsTheProblemDeclaresThemInTheOrderOfTheNamesTest()
    {
        var problem = SmtSanitizer.Sanitize(PROBLEM).Problem!;

        var refusal = SmtProjection.Take(problem, ["|y|", " |a b| ", "x"], out var constants);

        Assert.Multiple(() =>
        {
            Assert.That(refusal, Is.Null);
            Assert.That(constants.Select(constant => constant.Symbol.ToString()), Is.EqualTo(new[] { "y", "|a b|", "x" }));
            Assert.That(constants.Select(constant => constant.Sort.ToString()), Is.EqualTo(new[] { "Int", "Bool", "Int" }));
        });
    }

    [Test]
    public void AFunctionWithArgumentsIsNoConstantTest()
    {
        var problem = SmtSanitizer.Sanitize(PROBLEM).Problem!;

        var refusal = SmtProjection.Take(problem, ["f"], out var constants);

        Assert.Multiple(() =>
        {
            Assert.That(refusal?.Code, Is.EqualTo(SmtRefusalCodes.UNKNOWN_CONSTANT));
            Assert.That(constants, Is.Empty, "a refused projection gives no constant");
        });
    }

    #endregion

    #region Node Tests

    [Test]
    public void AProjectionWithoutANameIsRefusedAsMissingTest()
    {
        var problem = SmtSanitizer.Sanitize(PROBLEM).Problem!;

        var refusal = SmtProjection.Take(problem, [], out _);

        Assert.Multiple(() =>
        {
            Assert.That(refusal?.Code, Is.EqualTo(SmtRefusalCodes.FIELD_MISSING));
            Assert.That(refusal?.Reason, Is.EqualTo("'project' is missing: an enumeration names the constants its models are told apart by"));
        });
    }

    [Test]
    public void MoreNamesThanARowMayHoldAreRefusedBeforeAnyIsReadTest()
    {
        var problem = SmtSanitizer.Sanitize(PROBLEM).Problem!;
        var names = Enumerable.Repeat("(", SmtProblemSetLimits.MAX_PROJECT + 1).ToList();

        var refusal = SmtProjection.Take(problem, names, out _);

        Assert.Multiple(() =>
        {
            Assert.That(refusal?.Code, Is.EqualTo(SmtRefusalCodes.FIELD_INVALID));
            Assert.That(refusal?.Reason, Is.EqualTo("'project' holds more than the 256 names allowed"));
        });
    }

    [Test]
    public void ANameThatIsNotThereIsNoTermTest()
    {
        var problem = SmtSanitizer.Sanitize(PROBLEM).Problem!;

        var refusal = SmtProjection.Take(problem, ["x", null], out _);

        Assert.Multiple(() =>
        {
            Assert.That(refusal?.Code, Is.EqualTo(SmtRefusalCodes.NOT_A_TERM));
            Assert.That(refusal?.Reason, Does.StartWith("'project[1]': "));
        });
    }

    [Test]
    public void AProjectionNeedsAProblemAndNamesTest()
    {
        var problem = SmtSanitizer.Sanitize(PROBLEM).Problem!;

        Assert.Multiple(() =>
        {
            Assert.That(() => SmtProjection.Take(null!, ["x"], out _), Throws.ArgumentNullException);
            Assert.That(() => SmtProjection.Take(problem, null!, out _), Throws.ArgumentNullException);
        });
    }

    #endregion

    #region Case Tests

    [TestCaseSource(typeof(SmtProjectionCases), nameof(SmtProjectionCases.Ids))]
    public void ACaseGivesItsConstantsWhateverTheirSortTest(string id)
    {
        var one = SmtProjectionCases.Of(id);
        var problem = SmtSanitizer.Sanitize(one.Text).Problem!;

        var refusal = SmtProjection.Take(problem, [.. one.Project], out var constants);

        Assert.Multiple(() =>
        {
            Assert.That(refusal, Is.Null, () => $"{refusal?.Code}: {refusal?.Reason}");
            Assert.That(constants.Select(constant => constant.Name), Is.EqualTo(one.Project));
        });
    }

    #endregion
}
