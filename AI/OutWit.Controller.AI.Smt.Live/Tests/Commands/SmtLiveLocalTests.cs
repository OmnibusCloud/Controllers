using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OutWit.Controller.AI.Smt.Live.Commands;
using OutWit.Controller.AI.Smt.Live.Comparative;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Local;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Tests.Commands;

/// <summary>
/// <c>local</c> solving for real: the controller's sandbox, made once for the fixture (the module's
/// compile takes tens of seconds), and runs of a few problems each. The eight-queens kit gives the
/// very rows the test fleet gave (its <c>EXPECTED.md</c> records their digests and costs, which are
/// read from there); a handful of small problems of every query gives the same rows at one session
/// and at three.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SmtLiveLocalTests
{
    #region Constants

    /// <summary>The kit's expected result, whose last table records the rows the test fleet gave.</summary>
    private const string QUEENS_EXPECTED = "AI/OutWit.Controller.AI.Smt.Live/demo/02-eight-queens/EXPECTED.md";

    /// <summary>The kit's rows, in the set's order.</summary>
    private static readonly string[] QUEENS = ["eight-queens", "latin-5x5"];

    private const string QUEENS_SHA256 = "8fcb09369a7fc3a9ed2d59eac093769fdd52d0d66359de1cf83244dca707fd08";

    #endregion

    #region Fields

    private string m_root = null!;
    private SmtLiveSandbox m_sandbox = null!;
    private StringWriter m_output = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        SmtLiveSolver.Require();
        m_root = SmtLiveFixtures.NewFolder();

        var sandbox = SmtLiveSandbox.Open(Path.Combine(m_root, "sandbox"), out var problem);
        if (sandbox == null)
            SmtLiveSolver.Missing($"the controller's sandbox cannot start here: {problem}");

        m_sandbox = sandbox!;
        TestContext.Progress.WriteLine($"the sandbox: the module compiled in {m_sandbox.Compile.TotalSeconds:0.0} s");
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        m_sandbox?.Dispose();
        if (m_root != null)
            SmtLiveFixtures.Delete(m_root);
    }

    [SetUp]
    public void Setup()
    {
        m_output = new StringWriter();
    }

    [TearDown]
    public void TearDown()
    {
        TestContext.Out.WriteLine(m_output.ToString());
        m_output.Dispose();
    }

    #endregion

    #region Tests

    [Test]
    public async Task TheEightQueensGiveTheRowsTheFleetGaveTest()
    {
        var set = Path.Combine(TestContext.CurrentContext.TestDirectory, "Demo", "eight-queens.jsonl");
        var folder = Path.Combine(m_root, "queens");

        var exit = await Local(set, new SmtOptionsData(), null, folder);

        var rows = Rows(folder);
        var expected = File.ReadAllText(Path.Combine(SmtLiveFixtures.RepositoryRoot(), QUEENS_EXPECTED)).ReplaceLineEndings("\n");
        using var local = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, SmtLiveLocalFacts.FILE)));
        var facts = local.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK), "four documents that hold, nothing alarming");
            Assert.That(rows, Has.Count.EqualTo(QUEENS.Length));
            for (var index = 0; index < QUEENS.Length; index++)
            {
                var id = QUEENS[index];
                var (digest, fuel, resourceCount, checks) = FleetRow(expected, id);
                var row = rows[index];
                Assert.That((string?)row["id"], Is.EqualTo(id));
                Assert.That((string?)row["digest"], Is.EqualTo(digest), $"{id}: the digest the fleet gave");
                Assert.That((long?)row["stats"]!["fuel"], Is.EqualTo(fuel), id);
                Assert.That((long?)row["stats"]!["resource_count"], Is.EqualTo(resourceCount), id);
                Assert.That((long?)row["stats"]!["checks"], Is.EqualTo(checks), id);
                Assert.That((string?)row["validation"], Is.EqualTo("validated"), id);
                Assert.That((int?)row["copies"], Is.EqualTo(1), $"{id}: one machine, one copy");
                Assert.That((bool?)row["agreed"], Is.True, id);
                Assert.That((string?)row["settled_by"], Is.EqualTo("one_copy"), id);
            }

            Assert.That((int?)rows[0]["count"], Is.EqualTo(92));
            Assert.That((int?)rows[1]["count"], Is.EqualTo(1));

            Assert.That(facts.GetProperty("schema").GetString(), Is.EqualTo(SmtLiveLocalFacts.SCHEMA));
            Assert.That(facts.GetProperty("problem_set").GetProperty("sha256").GetString(), Is.EqualTo(QUEENS_SHA256));
            Assert.That(facts.GetProperty("problem_set").GetProperty("taken").GetInt32(), Is.EqualTo(2));
            Assert.That(facts.GetProperty("solver").GetProperty("module_pinned").GetBoolean(), Is.True);
            Assert.That(facts.GetProperty("solver").GetProperty("natives_pinned").GetBoolean(), Is.True);
            Assert.That(facts.GetProperty("degree").GetProperty("sessions").GetInt32(), Is.EqualTo(facts.GetProperty("degree").GetProperty("rule_sessions").GetInt32()),
                "the node's rule when no degree is given");
            Assert.That(facts.GetProperty("seconds").GetProperty("wall").GetDouble(), Is.GreaterThan(0));
            Assert.That(facts.GetProperty("seconds").GetProperty("solve").GetDouble(), Is.GreaterThan(0));
            Assert.That(facts.GetProperty("problems_per_second").GetDouble(), Is.GreaterThan(0));
            Assert.That(facts.GetProperty("machine").EnumerateObject().Select(member => member.Name),
                Is.EqualTo(new[] { "os", "os_version", "platform", "architecture", "cores", "memory_bytes", "available_bytes" }), "the machine without a name");

            Assert.That(Directory.Exists(Path.Combine(folder, SmtLiveLocal.WORK_FOLDER)), Is.False, "the working files are gone");
            Assert.That(Directory.GetFiles(folder).Select(Path.GetFileName).Order(),
                Is.EqualTo(new[] { "integrity.json", "local.json", "provenance.json", "report.json", "results.jsonl" }));
            Assert.That(m_output.ToString(), Does.Contain("result   : OK"));
            Assert.That(m_output.ToString(), Does.Contain("sampling  : rate 0, none"));
        });
    }

    [Test]
    public async Task AHandfulOfProblemsOfEveryQueryGiveTheSameRowsAtOneSessionAndAtThreeTest()
    {
        var set = Path.Combine(m_root, "handful.jsonl");
        await File.WriteAllLinesAsync(set, Handful());

        var one = Path.Combine(m_root, "handful-1");
        var three = Path.Combine(m_root, "handful-3");
        var exitOne = await Local(set, new SmtOptionsData(), 1, one);
        var exitThree = await Local(set, new SmtOptionsData(), 3, three);

        var rows = Rows(three).ToDictionary(row => (string)row["id"]!);
        var comparison = SmtLiveRowComparison.Of(Path.Combine(one, SmtLiveDocuments.RESULTS.File), Path.Combine(three, SmtLiveDocuments.RESULTS.File));
        using var facts = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(three, SmtLiveLocalFacts.FILE)));
        Assert.Multiple(() =>
        {
            Assert.That(exitOne, Is.EqualTo(SmtLiveExitCodes.OK));
            Assert.That(exitThree, Is.EqualTo(SmtLiveExitCodes.OK));
            Assert.That(comparison.AllSame, Is.True, string.Join("; ", comparison.Shown));
            Assert.That(comparison.WithDigests, Is.EqualTo(5), "every taken problem has a digest");

            Assert.That(Words(rows["lia-sat"]), Is.EqualTo("ok sat validated"));
            Assert.That(Words(rows["bv-unsat"]), Is.EqualTo("ok unsat none"));
            Assert.That($"{rows["lia-values"]["status"]} {rows["lia-values"]["verdict"]}", Is.EqualTo("ok sat"));
            Assert.That((string?)rows["lia-values"]["values"], Does.Contain("4"));
            Assert.That((string?)rows["prove-lia"]["proof"], Is.EqualTo("proved"));
            Assert.That(Words(rows["enum-three"]), Is.EqualTo("ok sat validated"));
            Assert.That((int?)rows["enum-three"]["count"], Is.EqualTo(3));
            Assert.That((bool?)rows["enum-three"]["complete"], Is.True);
            Assert.That((string?)rows["broken"]["status"], Is.EqualTo("refused"));

            Assert.That(facts.RootElement.GetProperty("degree").GetProperty("sessions").GetInt32(), Is.EqualTo(3));
            Assert.That(facts.RootElement.GetProperty("degree").GetProperty("decided_by").GetString(), Is.EqualTo(SmtLiveLocal.GIVEN));
            Assert.That(facts.RootElement.GetProperty("problem_set").GetProperty("refused").GetInt32(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task OptionsOutOfRangeAreRefusedBeforeAnythingRunsTest()
    {
        var set = Path.Combine(TestContext.CurrentContext.TestDirectory, "Demo", "eight-queens.jsonl");
        var folder = Path.Combine(m_root, "refused");

        var exit = await Local(set, new SmtOptionsData { HardCapSeconds = 601 }, null, folder);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.USAGE));
        Assert.That(m_output.ToString(), Does.Contain("'hard_cap_s' must be between 1 and 600"));
        Assert.That(Directory.Exists(folder), Is.False, "nothing was written");
    }

    #endregion

    #region Tools

    private Task<int> Local(string set, SmtOptionsData options, int? degree, string folder)
    {
        return new SmtLiveLocal(m_output, SmtLiveClock.StartNow(), m_sandbox).RunAsync(set, options, degree, folder, CancellationToken.None);
    }

    /// <summary>
    /// A row the test fleet gave, as the kit's <c>EXPECTED.md</c> records it:
    /// <c>| `id`: digest, fuel, resource count, checks... | `digest`, fuel, resource count, checks... |</c>.
    /// </summary>
    private static (string Digest, long Fuel, long ResourceCount, long Checks) FleetRow(string expected, string id)
    {
        var line = expected.Split('\n').SingleOrDefault(candidate => candidate.StartsWith($"| `{id}`: digest", StringComparison.Ordinal));
        Assert.That(line, Is.Not.Null, $"{QUEENS_EXPECTED} records the fleet's row of {id}");

        var match = Regex.Match(line!, @"`([0-9a-f]{64})`, ([0-9,]+), ([0-9,]+), ([0-9,]+)");
        Assert.That(match.Success, Is.True, $"the fleet's row of {id}: a digest, the fuel, the resource count and the checks");

        static long Number(Group group) => long.Parse(group.Value.Replace(",", ""), CultureInfo.InvariantCulture);
        return (match.Groups[1].Value, Number(match.Groups[2]), Number(match.Groups[3]), Number(match.Groups[4]));
    }

    private static List<JsonObject> Rows(string folder)
    {
        return File.ReadAllLines(Path.Combine(folder, SmtLiveDocuments.RESULTS.File))
            .Where(line => line.Length > 0)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .ToList();
    }

    private static string Words(JsonObject row)
    {
        return $"{row["status"]} {row["verdict"]} {row["validation"]}";
    }

    /// <summary>Small problems of every query, and a row the set refuses.</summary>
    private static IEnumerable<string> Handful()
    {
        yield return Row(new JsonObject
        {
            ["id"] = "lia-sat", ["expected"] = "sat",
            ["smt2"] = "(set-logic QF_LIA)\n(declare-const x Int)\n(declare-const y Int)\n(assert (> x 2))\n(assert (< y 5))\n(assert (= (+ x y) 10))\n"
        });
        yield return Row(new JsonObject
        {
            ["id"] = "bv-unsat", ["expected"] = "unsat",
            ["smt2"] = "(set-logic QF_BV)\n(declare-const a (_ BitVec 8))\n(assert (bvult a #x05))\n(assert (bvugt a #x07))\n"
        });
        yield return Row(new JsonObject
        {
            ["id"] = "lia-values", ["values"] = new JsonArray("x"),
            ["smt2"] = "(set-logic QF_LIA)\n(declare-const x Int)\n(assert (= (* 3 x) 12))\n"
        });
        yield return Row(new JsonObject
        {
            ["id"] = "prove-lia", ["query"] = "prove", ["goal"] = "(> (+ x 1) x)",
            ["smt2"] = "(set-logic QF_LIA)\n(declare-const x Int)\n"
        });
        yield return Row(new JsonObject
        {
            ["id"] = "enum-three", ["query"] = "enumerate", ["project"] = new JsonArray("x"), ["max_models"] = 5, ["expected"] = "sat",
            ["smt2"] = "(set-logic QF_LIA)\n(declare-const x Int)\n(assert (and (<= 0 x) (< x 3)))\n"
        });
        yield return Row(new JsonObject
        {
            ["id"] = "broken",
            ["smt2"] = "(set-logic QF_LIA)\n(assert (> x 1)\n"
        });
    }

    private static string Row(JsonObject row)
    {
        return row.ToJsonString();
    }

    #endregion
}
