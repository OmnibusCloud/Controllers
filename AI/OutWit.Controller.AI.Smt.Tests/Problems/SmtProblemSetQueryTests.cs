using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// What is asked of a problem: the query of a row or of the run, the fields that belong to each
/// query, the terms and the names beside the problem, and the verdict a row expects.
/// </summary>
[TestFixture]
public class SmtProblemSetQueryTests
{
    #region Constants

    private static readonly string[] QUERIES = ["check", "enumerate", "prove"];

    /// <summary>The fields a query cannot do without, as JSON; empty for a check.</summary>
    private static readonly Dictionary<string, string> NEEDED = new(StringComparer.Ordinal)
    {
        ["check"] = "",
        ["enumerate"] = "\"project\":[\"x\"]",
        ["prove"] = "\"goal\":\"(> x y)\""
    };

    /// <summary>Every field that belongs to some queries only: a value it takes, the queries it belongs to, and the words for their rows.</summary>
    private static readonly (string Field, string Value, string[] Queries, string Rows)[] OWNED =
    [
        ("values", "[\"x\"]", ["check"], "a check row"),
        ("project", "[\"y\"]", ["enumerate"], "an enumerate row"),
        ("max_models", "5", ["enumerate"], "an enumerate row"),
        ("goal", "\"(< x y)\"", ["prove"], "a prove row"),
        ("expected", "\"sat\"", ["check", "enumerate"], "a check or an enumerate row")
    ];

    #endregion

    #region Field Table Tests

    [Test]
    public void EveryFieldIsTakenByItsQueriesAndRefusedByTheOthersTest()
    {
        foreach (var query in QUERIES)
        {
            foreach (var (field, value, queries, rows) in OWNED)
            {
                // The field once in place of the row's own (a row has a field once), once beside it.
                var needed = NEEDED[query].StartsWith($"\"{field}\"", StringComparison.Ordinal) ? "" : NEEDED[query];
                var fields = string.Join(",", new[] { needed, $"\"{field}\":{value}" }.Where(part => part.Length > 0));

                foreach (var named in new[] { true, false })
                {
                    var line = SmtProblemSets.Row(named ? $"\"query\":\"{query}\",{fields}" : fields);
                    var options = new SmtOptionsData { Query = Enum.Parse<SmtQuery>(query, ignoreCase: true) };
                    var what = $"{field} in a {query} row, the query {(named ? "named" : "the run's")}";

                    if (queries.Contains(query))
                    {
                        Assert.That(SmtProblemSets.Accept(line, options).Query.ToString().ToLowerInvariant(), Is.EqualTo(query), what);
                        continue;
                    }

                    var refusal = SmtProblemSets.Refuse(line, SmtRefusalCodes.FIELD_NOT_ALLOWED, options);
                    Assert.That(refusal.Reason, Is.EqualTo($"'{field}' is a field of {rows}; the query of this row is {query}{(named ? "" : ", the run's default")}"), what);
                    Assert.That((refusal.Row, refusal.Line, refusal.File), Is.EqualTo((1, 0, "a")), what);
                }
            }
        }
    }

    [Test]
    public void RowHasTheFieldsItsQueryNeedsTest()
    {
        foreach (var named in new[] { true, false })
        {
            var enumerate = SmtProblemSets.Refuse(SmtProblemSets.Row(named ? "\"query\":\"enumerate\"" : ""), SmtRefusalCodes.FIELD_MISSING, new SmtOptionsData { Query = SmtQuery.Enumerate });
            var prove = SmtProblemSets.Refuse(SmtProblemSets.Row(named ? "\"query\":\"prove\"" : ""), SmtRefusalCodes.FIELD_MISSING, new SmtOptionsData { Query = SmtQuery.Prove });

            Assert.That(enumerate.Reason, Is.EqualTo("'project' is missing: an enumerate row names the constants its models are told apart by"));
            Assert.That(prove.Reason, Is.EqualTo("'goal' is missing: a prove row names what is to be proved"));
            Assert.That((prove.Row, prove.Line, prove.File), Is.EqualTo((1, 0, "a")));
        }

        // A check needs nothing beside its problem.
        Assert.That(SmtProblemSets.Accept(SmtProblemSets.Row()).Query, Is.EqualTo(SmtQuery.Check));
    }

    [Test]
    public void RowsOwnQueryHoldsOverTheRunsTest()
    {
        foreach (var run in Enum.GetValues<SmtQuery>())
        {
            foreach (var query in QUERIES)
            {
                var needed = NEEDED[query].Length > 0 ? "," + NEEDED[query] : "";
                var data = SmtProblemSets.Accept(SmtProblemSets.Row($"\"query\":\"{query}\"{needed}"), new SmtOptionsData { Query = run });

                Assert.That(data.Query.ToString().ToLowerInvariant(), Is.EqualTo(query));
            }
        }
    }

    [Test]
    public void FieldsAreHeldToTheQueryBeforeTheProblemIsLookedAtTest()
    {
        const string broken = "{\"id\":\"a\",\"smt2\":\"(push 1)\"";

        Assert.That(SmtProblemSets.Refuse(broken + ",\"goal\":\"p\"}", SmtRefusalCodes.FIELD_NOT_ALLOWED).Reason, Does.StartWith("'goal' is a field of a prove row"));
        Assert.That(SmtProblemSets.Refuse(broken + ",\"query\":\"enumerate\"}", SmtRefusalCodes.FIELD_MISSING).Reason, Does.StartWith("'project' is missing"));
        Assert.That(SmtProblemSets.Refuse(broken + ",\"query\":\"prove\",\"values\":[\"x\"]}", SmtRefusalCodes.FIELD_NOT_ALLOWED).Reason, Does.StartWith("'values' is a field of a check row"));
        Assert.That(SmtProblemSets.Refuse(broken + "}", SmtRefusalCodes.COMMAND_NOT_ALLOWED).Command, Is.EqualTo("push"));
    }

    #endregion

    #region Default Tests

    [TestCase(0, null, 2)]
    [TestCase(7, null, 7)]
    [TestCase(1000, null, 1000)]
    [TestCase(0, 3, 3)]
    [TestCase(7, 3, 3)]
    [TestCase(7, 1000, 1000)]
    public void NumberOfModelsIsTheRowsElseTheRunsElseTwoTest(int run, int? row, int models)
    {
        var line = SmtProblemSets.Row("\"query\":\"enumerate\",\"project\":[\"x\"]" + (row == null ? "" : $",\"max_models\":{row}"));

        Assert.That(SmtProblemSets.Accept(line, new SmtOptionsData { MaxModels = run }).MaxModels, Is.EqualTo(models));
    }

    [Test]
    public void NumberOfModelsIsOfAnEnumerationOnlyTest()
    {
        var options = new SmtOptionsData { MaxModels = 9 };

        Assert.That(SmtProblemSets.Accept(SmtProblemSets.Row(), options).MaxModels, Is.Zero);
        Assert.That(SmtProblemSets.Accept(SmtProblemSets.Row("\"query\":\"prove\",\"goal\":\"(> x y)\""), options).MaxModels, Is.Zero);
    }

    #endregion

    #region Expected Tests

    [TestCase("", "", SmtProblemStatus.None)]
    [TestCase("(set-info :status sat)", "", SmtProblemStatus.Sat)]
    [TestCase("(set-info :status unsat)", "", SmtProblemStatus.Unsat)]
    [TestCase("(set-info :status unknown)", "", SmtProblemStatus.None)]
    [TestCase("", "\"expected\":\"unsat\"", SmtProblemStatus.Unsat)]
    [TestCase("(set-info :status sat)", "\"expected\":\"unsat\"", SmtProblemStatus.Unsat)]
    [TestCase("(set-info :status unsat)", "\"expected\":\"sat\"", SmtProblemStatus.Sat)]
    [TestCase("(set-info :status unknown)", "\"expected\":\"sat\"", SmtProblemStatus.Sat)]
    public void ExpectedVerdictIsTheRowsElseTheProblemsStatusTest(string status, string expected, SmtProblemStatus verdict)
    {
        var problem = $"{status}(declare-const x Int)(assert (> x 0))";
        var tail = expected.Length > 0 ? "," + expected : "";

        Assert.That(SmtProblemSets.Accept($"{{\"id\":\"a\",\"smt2\":\"{problem}\"{tail}}}").Expected, Is.EqualTo(verdict));
        Assert.That(SmtProblemSets.Accept($"{{\"id\":\"a\",\"smt2\":\"{problem}\",\"query\":\"enumerate\",\"project\":[\"x\"]{tail}}}").Expected, Is.EqualTo(verdict));
    }

    [Test]
    public void ProofExpectsNothingTest()
    {
        // The verdict of a proof is about its goal: the problem's own status says nothing of it.
        var data = SmtProblemSets.Accept("""{"id":"a","smt2":"(set-info :status sat)(declare-const p Bool)(assert p)","query":"prove","goal":"p"}""");

        Assert.That(data.Expected, Is.EqualTo(SmtProblemStatus.None));
        Assert.That(data.Text, Does.Contain("(set-info :status sat)"), "the status stays in the problem's text");
    }

    #endregion

    #region Term Tests

    [Test]
    public void ValuesAndGoalTravelAsTheirCanonicalTextTest()
    {
        var check = SmtProblemSets.Accept(SmtProblemSets.Row("\"values\":[\"x\",\" ( +   x\\n 1 ) ; a comment\",\"|y|\",\"(f |a b| \\\"s\\\"\\\"t\\\")\",\"42\"]"));
        var prove = SmtProblemSets.Accept(SmtProblemSets.Row("\"query\":\"prove\",\"goal\":\"(=>  (> x y)\\n\\t(>= x y))\""));

        Assert.That(check.Values, Is.EqualTo(new[] { "x", "(+ x 1)", "y", "(f |a b| \"s\"\"t\")", "42" }));
        Assert.That(prove.Goal, Is.EqualTo("(=> (> x y) (>= x y))"));
    }

    [TestCase("x y", SmtRefusalCodes.NOT_A_TERM, 1, "a term is one s-expression; the text holds more than one")]
    [TestCase("", SmtRefusalCodes.NOT_A_TERM, 1, "a term is one s-expression; the text holds none")]
    [TestCase("(> x", SmtRefusalCodes.SYNTAX_ERROR, 1, "a list is not closed")]
    [TestCase("(> x\\n -5)", SmtRefusalCodes.AMBIGUOUS_TOKEN, 2, "'-5' is a symbol by the standard and a negative number to the solver; a negative number is written (- 5)")]
    [TestCase("(and p\\n\\n |let|)", SmtRefusalCodes.RESERVED_SYMBOL, 3, "the reserved word 'let' is written between bars: there it is an ordinary symbol by the standard and the reserved word to the solver, so it cannot be a name")]
    [TestCase("oc!x", SmtRefusalCodes.RESERVED_PREFIX, 1, "the name 'oc!x' starts with 'oc!', the prefix of the controller's own names")]
    [TestCase("(_ bv0 99999)", SmtRefusalCodes.INDEX_TOO_LARGE, 1, "the index 99999 is above the 65536 allowed")]
    public void TermTheSanitizerRefusesRefusesTheRowWithItsOwnCodeTest(string term, string code, int line, string reason)
    {
        var inValues = SmtProblemSets.Refuse(SmtProblemSets.Row($"\"values\":[\"x\",\"{term}\"]"), code);
        var inGoal = SmtProblemSets.Refuse(SmtProblemSets.Row($"\"query\":\"prove\",\"goal\":\"{term}\""), code);
        var inProject = SmtProblemSets.Refuse(SmtProblemSets.Row($"\"query\":\"enumerate\",\"project\":[\"{term}\"]"), code);

        Assert.That(inValues.Reason, Is.EqualTo($"'values[1]': {reason}"));
        Assert.That(inGoal.Reason, Is.EqualTo($"'goal': {reason}"));
        Assert.That(inProject.Reason, Is.EqualTo($"'project[0]': {reason}"));

        foreach (var refusal in new[] { inValues, inGoal, inProject })
            Assert.That((refusal.Row, refusal.Line, refusal.File, refusal.Command), Is.EqualTo((1, line, "a", "")), "the line is the line inside the term");
    }

    [Test]
    public void TermLongerThanATermMayBeRefusesTheRowTest()
    {
        var term = "(and" + string.Concat(Enumerable.Repeat(" a", SmtSanitizer.TERM_CHARACTERS / 2)) + ")";

        var refusal = SmtProblemSets.Refuse(SmtProblemSets.Row($"\"query\":\"prove\",\"goal\":\"{term}\""), SmtRefusalCodes.TEXT_TOO_LONG);

        Assert.That(refusal.Reason, Does.StartWith("'goal': the text has "));
    }

    #endregion

    #region Projection Tests

    [Test]
    public void ProjectionNamesConstantsTheProblemDeclaresAsItDeclaresThemTest()
    {
        const string problem = "(declare-const x Int)(declare-fun y () Bool)(declare-const |a b| Int)(declare-const exit Bool)(declare-const |quoted| Int)";

        var data = SmtProblemSets.Accept($"{{\"id\":\"a\",\"smt2\":\"{problem}\",\"query\":\"enumerate\",\"project\":[\"|x|\",\" y \",\"|a b|\",\"|exit|\",\"quoted\"]}}");

        Assert.That(data.Project, Is.EqualTo(new[] { "x", "y", "|a b|", "exit", "quoted" }));
    }

    [TestCase("z")]
    [TestCase("f")]
    [TestCase("c")]
    [TestCase("mk")]
    [TestCase("X")]
    [TestCase("|x |")]
    [TestCase("true")]
    public void NameThatIsNoDeclaredConstantRefusesTheRowTest(string name)
    {
        // Declared with arguments, defined, a constructor, in another case: none is a constant the problem declares.
        const string problem = "(declare-const x Int)(declare-fun f (Int) Int)(define-fun c () Int 1)(declare-datatype D ((mk)))";

        var refusal = SmtProblemSets.Refuse($"{{\"id\":\"a\",\"smt2\":\"{problem}\",\"query\":\"enumerate\",\"project\":[\"x\",\"{name}\"]}}", SmtRefusalCodes.UNKNOWN_CONSTANT);

        Assert.That(refusal.Reason, Is.EqualTo($"'project[1]' names '{name.Trim('|')}', which the problem does not declare as a constant"));
        Assert.That((refusal.Row, refusal.Line, refusal.File), Is.EqualTo((1, 0, "a")));
    }

    [TestCase("(+ x 1)")]
    [TestCase("(x)")]
    [TestCase("5")]
    [TestCase("\\\"x\\\"")]
    [TestCase(":x")]
    [TestCase("#b01")]
    public void TermThatIsNoNameRefusesTheRowTest(string term)
    {
        var refusal = SmtProblemSets.Refuse(SmtProblemSets.Row($"\"query\":\"enumerate\",\"project\":[\"{term}\"]"), SmtRefusalCodes.FIELD_INVALID);

        Assert.That(refusal.Reason, Is.EqualTo("'project[0]' must be the name of a constant: one symbol"));
    }

    [Test]
    public void ConstantNamedTwiceRefusesTheRowTest()
    {
        // Between bars or not, a name is one name.
        foreach (var names in new[] { "\"x\",\"y\",\"x\"", "\"x\",\"y\",\"|x|\"", "\"|x|\",\"y\",\" x \"" })
        {
            var refusal = SmtProblemSets.Refuse(SmtProblemSets.Row($"\"query\":\"enumerate\",\"project\":[{names}]"), SmtRefusalCodes.FIELD_INVALID);

            Assert.That(refusal.Reason, Is.EqualTo("'project[2]' names 'x' again: a constant is named once"));
        }
    }

    [Test]
    public void LongUnknownNameIsShownCutTest()
    {
        var refusal = SmtProblemSets.Refuse(SmtProblemSets.Row($"\"query\":\"enumerate\",\"project\":[\"{new string('n', 5_000)}\"]"), SmtRefusalCodes.UNKNOWN_CONSTANT);

        Assert.That(refusal.Reason, Is.EqualTo("'project[0]' names '" + new string('n', 61) + "...', which the problem does not declare as a constant"));
    }

    #endregion
}
