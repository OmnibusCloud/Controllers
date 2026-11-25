using System.Text.Json;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Settlement;

/// <summary>
/// The terms the host holds a row's values to are the terms a node's session asks the solver
/// for, written the same way: the host makes them of the problem as it travelled, by its own
/// reading, and the two readings must not drift apart, or an honest row's values would be taken
/// for the answer of something else.
/// </summary>
[TestFixture]
public sealed class SmtAskedTermsTests
{
    #region Constants

    private const string PROBLEM =
        "(declare-const x Int)(declare-const |a b| Int)(declare-const |exit| Int)(declare-const s String)(declare-fun f (Int) Int)"
        + "(assert (> x |a b|))(assert (> exit 0))";

    #endregion

    #region Tests

    [TestCase("x")]
    [TestCase("|x|", "|a b|", "exit")]
    [TestCase("(+ x |a b|)", "(f 1)", "( f   x )", "(str.++ s \"a\"\"b\")", "(let ((z x)) (+ z 1.50 #x0F #b01))")]
    [TestCase("(! x :named n)", "((_ divisible 3) x)", "(- 5)", "true")]
    public void HostHoldsACheckToTheTermsItsSessionAsksTest(params string[] values)
    {
        var problem = SmtHostChecks.Problem(Line(new() { ["id"] = "p0", ["smt2"] = PROBLEM, ["values"] = values }));

        var host = SmtHostChecks.AskedByTheHost(problem);

        Assert.That(host, Is.Not.Null);
        Assert.That(host, Has.Count.EqualTo(values.Length));
        Assert.That(host, Is.EqualTo(SmtHostChecks.AskedByASession(problem)));
    }

    [TestCase("x")]
    [TestCase("|x|", "|a b|", "exit")]
    [TestCase("|exit|", "x")]
    public void HostHoldsAnEnumerationToTheConstantsItsSessionProjectsOnTest(params string[] project)
    {
        // Each constant as the problem declares it, however the row spells its name.
        var problem = SmtHostChecks.Problem(Line(new() { ["id"] = "p0", ["query"] = "enumerate", ["smt2"] = PROBLEM, ["project"] = project }));

        var host = SmtHostChecks.AskedByTheHost(problem);

        Assert.That(host, Is.Not.Null);
        Assert.That(host, Has.Count.EqualTo(project.Length));
        Assert.That(host, Is.EqualTo(SmtHostChecks.AskedByASession(problem)));
    }

    [Test]
    public void ProofAndCheckWithoutValuesAskForNoneTest()
    {
        var check = SmtHostChecks.Problem(Line(new() { ["id"] = "p0", ["smt2"] = PROBLEM }));
        var proof = SmtHostChecks.Problem(Line(new() { ["id"] = "p0", ["query"] = "prove", ["smt2"] = PROBLEM, ["goal"] = "(> x 0)" }));

        Assert.That(SmtHostChecks.AskedByTheHost(check), Is.Empty);
        Assert.That(SmtHostChecks.AskedByTheHost(proof), Is.Empty);
        Assert.That(SmtHostChecks.AskedByASession(check), Is.Empty);
    }

    [Test]
    public void TermsThatNoSessionWouldAskAreNotMadeUpTest()
    {
        // A problem as it travels is the host's own; one whose terms are not terms, or whose
        // projection names what the problem does not declare, has no session that asks for values.
        var check = SmtHostChecks.Problem(Line(new() { ["id"] = "p0", ["smt2"] = PROBLEM, ["values"] = new[] { "x" } }));
        var enumeration = SmtHostChecks.Problem(Line(new() { ["id"] = "p0", ["query"] = "enumerate", ["smt2"] = PROBLEM, ["project"] = new[] { "x" } }));

        check.Values = ["x", "(+ x"];
        Assert.That(SmtHostChecks.AskedByTheHost(check), Is.Null);
        Assert.That(SmtHostChecks.AskedByASession(check), Is.Null);

        enumeration.Project = ["nowhere"];
        Assert.That(SmtHostChecks.AskedByTheHost(enumeration), Is.Null);
        Assert.That(SmtHostChecks.AskedByASession(enumeration), Is.Null);

        enumeration.Project = ["(+ x 1)"];
        Assert.That(SmtHostChecks.AskedByTheHost(enumeration), Is.Null);
        Assert.That(SmtHostChecks.AskedByASession(enumeration), Is.Null);
    }

    #endregion

    #region Tools

    private static string Line(Dictionary<string, object> row)
    {
        return JsonSerializer.Serialize(row);
    }

    #endregion
}
