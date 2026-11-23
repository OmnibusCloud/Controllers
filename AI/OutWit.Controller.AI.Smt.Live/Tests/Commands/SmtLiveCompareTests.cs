using System.Text.Json.Nodes;
using OutWit.Controller.AI.Smt.Live.Commands;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Live.Tests.Commands;

[TestFixture]
public sealed class SmtLiveCompareTests
{
    #region Fields

    private string m_root = null!;
    private string m_left = null!;
    private string m_right = null!;
    private StringWriter m_output = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = SmtLiveFixtures.NewFolder();
        m_left = SmtLiveFixtures.CopyRun(Path.Combine(m_root, "run-1"));
        m_right = SmtLiveFixtures.CopyRun(Path.Combine(m_root, "run-2"));
        m_output = new StringWriter();
    }

    [TearDown]
    public void TearDown()
    {
        TestContext.Out.WriteLine(m_output.ToString());
        SmtLiveFixtures.Delete(m_root);
        m_output.Dispose();
    }

    #endregion

    #region Tests

    [Test]
    public void TwoRunsWithTheSameDocumentsAreTheSameTest()
    {
        var exit = Compare();

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK));
        Assert.That(m_output.ToString(), Does.Contain("4 compared").And.Contain("all the same").And.Contain("provenance: the same"));
    }

    [Test]
    public void WhereTheWorkWasPlacedIsLeftOutTest()
    {
        SmtLiveFixtures.EditRow(Results(m_right), 1, row =>
        {
            row["copies"] = 1;
            row["settled_by"] = "one_copy";
        });
        SmtLiveFixtures.EditRow(Results(m_right), 2, row =>
        {
            row["copies"] = 5;
            row["agreed"] = false;
            row["settled_by"] = "dispute";
        });

        Assert.That(Compare(), Is.EqualTo(SmtLiveExitCodes.OK));
        Assert.That(SmtLiveCompare.PLACEMENT, Is.EquivalentTo(new[] { "copies", "agreed", "settled_by" }));
    }

    [Test]
    public void ARowWhoseAnswerDiffersIsPrintedTest()
    {
        SmtLiveFixtures.EditRow(Results(m_right), 1, row =>
        {
            row["verdict"] = "sat";
            row["digest"] = new string('d', 64);
        });
        SmtLiveFixtures.EditRow(Results(m_right), 2, row => row["models"]!.AsArray()[1]!.AsObject()["values"] = "((y 7))");
        SmtLiveFixtures.EditRow(Results(m_right), 0, row => row["stats"]!.AsObject()["fuel"] = 3401);

        var exit = Compare();

        var output = m_output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.FAILED));
            Assert.That(output, Does.Contain("3 DIFFER"));
            Assert.That(output, Does.Contain("[1] b: verdict: \"unsat\" vs \"sat\"; digest: "));
            Assert.That(output, Does.Contain("[2] c: models[1].values: \"((y 1))\" vs \"((y 7))\""));
            Assert.That(output, Does.Contain("[0] a: stats.fuel: 3400 vs 3401"));
            Assert.That(output, Does.Contain("THE RUNS DIFFER: 3 of 4 row(s)"));
        });
    }

    [Test]
    public void ARowOnlyOneRunHasIsADifferenceTest()
    {
        var lines = File.ReadAllLines(Results(m_right)).Where(line => line.Length > 0).ToList();
        File.WriteAllText(Results(m_right), string.Join("\n", lines.Take(3)) + "\n");

        Assert.That(Compare(), Is.EqualTo(SmtLiveExitCodes.FAILED));
        Assert.That(m_output.ToString(), Does.Contain("row 4: only in the first run"));
    }

    [Test]
    public void ADifferentProvenanceIsPrintedByPathTest()
    {
        SmtLiveFixtures.Edit(Path.Combine(m_right, SmtLiveDocuments.PROVENANCE.File), root =>
        {
            root["options"]!.AsObject()["seed"] = 7;
            root["problem_set"]!.AsObject()["sha256"] = new string('6', 64);
        });

        var exit = Compare();

        var output = m_output.ToString();
        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.FAILED));
        Assert.That(output, Does.Contain("provenance: DIFFERS").And.Contain("$.options.seed: 0 vs 7").And.Contain("$.problem_set.sha256: "));
        Assert.That(output, Does.Contain("and the provenance"));
    }

    [Test]
    public void NumbersAreComparedByTheirValueAndFieldsWhateverTheirOrderTest()
    {
        SmtLiveFixtures.Edit(Path.Combine(m_right, SmtLiveDocuments.PROVENANCE.File), root =>
        {
            root["sampling_rate"] = JsonValue.Create(1.0m);
            var reordered = new JsonObject();
            foreach (var pair in root["profile"]!.AsObject().Reverse().ToList())
                reordered[pair.Key] = pair.Value?.DeepClone();

            root["profile"] = reordered;
        });

        Assert.That(Compare(), Is.EqualTo(SmtLiveExitCodes.OK));
    }

    [Test]
    public void AFolderWithoutTheDocumentsIsAUsageErrorTest()
    {
        File.Delete(Path.Combine(m_right, SmtLiveDocuments.PROVENANCE.File));

        Assert.That(Compare(), Is.EqualTo(SmtLiveExitCodes.USAGE));
        Assert.That(m_output.ToString(), Does.Contain("provenance.json is not there"));
    }

    #endregion

    #region Tools

    private int Compare()
    {
        return new SmtLiveCompare(m_output).Run(m_left, m_right);
    }

    private static string Results(string folder)
    {
        return Path.Combine(folder, SmtLiveDocuments.RESULTS.File);
    }

    #endregion
}
