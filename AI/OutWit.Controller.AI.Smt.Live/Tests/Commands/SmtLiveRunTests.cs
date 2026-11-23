using System.Text.Json.Nodes;
using OutWit.Controller.AI.Smt.Live.Commands;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Tests.Commands;

[TestFixture]
public sealed class SmtLiveRunTests
{
    #region Fields

    private string m_root = null!;
    private string m_documents = null!;
    private string m_set = null!;
    private string m_out = null!;
    private SmtLiveServerFake m_server = null!;
    private StringWriter m_output = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = SmtLiveFixtures.NewFolder();
        m_documents = SmtLiveFixtures.CopyRun(Path.Combine(m_root, "server"));
        m_set = Path.Combine(m_root, "problems.jsonl");
        m_out = Path.Combine(m_root, "run");
        File.WriteAllText(m_set, "{\"id\":\"a\",\"smt2\":\"(declare-const x Int)(assert (> x 1))\"}\n\n{\"id\":\"b\",\"smt2\":\"(assert false)\"}\n");
        m_server = new SmtLiveServerFake();
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
    public async Task ARunUploadsTheSetAndChecksAndSummarisesTheFourDocumentsTest()
    {
        Answer();
        var options = new SmtOptionsData { ResourceLimit = 2_000_000_000, HardCapSeconds = 100, SamplingRate = 1 };

        var exit = await Run(options);

        var output = m_output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK));
            Assert.That(m_server.Submitted, Has.Count.EqualTo(1));
            Assert.That(m_server.Submitted[0].Script, Is.EqualTo("SmtRun"));
            Assert.That(m_server.Submitted[0].Parameters, Has.Count.EqualTo(2));
            Assert.That(m_server.Submitted[0].Parameters[0], Is.EqualTo(m_server.Uploaded.Single()), "the set travels as a blob");
            Assert.That(((SmtOptionsData)m_server.Submitted[0].Parameters[1]!).Is(options), Is.True, "the options as given");
            Assert.That(m_server.Blobs[m_server.Uploaded.Single()], Is.EqualTo(File.ReadAllBytes(m_set)));

            foreach (var document in SmtLiveDocuments.RUN)
                Assert.That(File.ReadAllBytes(Path.Combine(m_out, document.File)), Is.EqualTo(File.ReadAllBytes(Path.Combine(m_documents, document.File))), document.File);

            Assert.That(output, Does.Contain("input    : problems.jsonl, 2 row(s)"));
            Assert.That(output, Does.Contain("rlimit 2000000000, hard cap 100 s"));
            Assert.That(output, Does.Contain("results.jsonl    4 row(s)").And.Contain("valid (smt-results.schema.json)"));
            Assert.That(output, Does.Contain("valid (smt-report.schema.json)").And.Contain("valid (smt-integrity.schema.json)").And.Contain("valid (smt-provenance.schema.json)"));
            Assert.That(output, Does.Contain("verdicts  : sat 2, unsat 1, unknown 0"));
            Assert.That(output, Does.Contain("expected  : agree 2, disagree 0, n/a 2"));
            Assert.That(output, Does.Contain("QF_LIA     validated 2, partial 0, failed 0"));
            Assert.That(output, Does.Contain("3 problem(s) answered by two machines or more, 3 of them agreed"));
            Assert.That(output, Does.Contain("lies      : 0 answer(s)"));
            Assert.That(output, Does.Contain("0 disagreements between the evaluator and the solver"));
            Assert.That(output, Does.Contain("[3] bad: refused syntax_error, line 2"));
            Assert.That(output, Does.Contain("result   : OK"));
            Assert.That(output, Does.Not.Contain(SmtLiveRunSummary.LOUD));
        });
    }

    [Test]
    public async Task WhatARunMustNotHaveIsSaidLoudlyAndMakesTheExitCodeThreeTest()
    {
        SmtLiveFixtures.Edit(Path.Combine(m_documents, SmtLiveDocuments.INTEGRITY.File), root =>
        {
            var summary = root["summary"]!.AsObject();
            summary["lies"] = 1;
            summary["problems_with_lies"] = 1;
            summary["evaluator_solver_disagreements"] = 1;
            root["evaluator_solver_disagreements"] = new JsonArray(new JsonObject { ["index"] = 2, ["reason"] = "assertion_false", ["detail"] = "model 2: 1" });
        });
        SmtLiveFixtures.EditRow(Path.Combine(m_documents, SmtLiveDocuments.RESULTS.File), 1, row => row["expected_agreement"] = "disagree");
        Answer();

        var exit = await Run(new SmtOptionsData());

        var output = m_output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.ALARM));
            Assert.That(output, Does.Contain("!!! lies      : 1 answer(s) in 1 problem(s)"));
            Assert.That(output, Does.Contain("!!! evaluator : EVALUATOR/SOLVER DISAGREEMENTS: 1 problem(s)"));
            Assert.That(output, Does.Contain("[2] c: assertion_false model 2: 1"), "the problem by its index and its id");
            Assert.That(output, Does.Contain("[1] b: verdict unsat, expected unsat"));
            Assert.That(output, Does.Contain("ALARM: 1 disagreement(s) between the evaluator and the solver; 1 lie(s) found"));
        });
    }

    [Test]
    public async Task ADocumentThatDoesNotHoldToItsSchemaFailsTheRunTest()
    {
        SmtLiveFixtures.Edit(Path.Combine(m_documents, SmtLiveDocuments.REPORT.File), root => root.Remove("verdicts"));
        Answer();

        var exit = await Run(new SmtOptionsData());

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.FAILED));
        Assert.That(m_output.ToString(), Does.Contain("report.json      ").And.Contain("DOES NOT HOLD TO smt-report.schema.json").And.Contain("$: lacks 'verdicts'"));
        Assert.That(m_output.ToString(), Does.Contain("result   : FAILED: a document does not hold to its schema"));
    }

    [Test]
    public async Task ADocumentThatDoesNotComeFailsTheRunTest()
    {
        Answer(SmtLiveDocuments.PROVENANCE);

        var exit = await Run(new SmtOptionsData());

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.FAILED));
        Assert.That(m_output.ToString(), Does.Contain("provenance.json  DID NOT COME"));
    }

    [Test]
    public async Task AJobThatFailsFailsTheRunTest()
    {
        m_server.Jobs = (_, _) => SmtLiveJobFake.Failed("Smt.Split: the problem set has no row");

        var exit = await Run(new SmtOptionsData());

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.FAILED));
        Assert.That(m_output.ToString(), Does.Contain("Failed after").And.Contain("the problem set has no row"));
        Assert.That(Directory.Exists(m_out), Is.False);
    }

    [Test]
    public async Task OptionsOutOfRangeAreRefusedBeforeAnythingIsSentTest()
    {
        var exit = await Run(new SmtOptionsData { SamplingRate = 2, HardCapSeconds = 601 });

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.USAGE));
        Assert.That(m_server.Uploaded, Is.Empty);
        Assert.That(m_server.Submitted, Is.Empty);
        Assert.That(m_output.ToString(), Does.Contain("'sampling_rate' must be between 0 and 1").And.Contain("'hard_cap_s' must be between 1 and 600"));
    }

    #endregion

    #region Tools

    private Task<int> Run(SmtOptionsData options)
    {
        return new SmtLiveRun(m_server, m_output).RunAsync(m_set, options, m_out, CancellationToken.None);
    }

    /// <summary>The job answers with the blobs of the documents in the server folder, but the ones left out.</summary>
    private void Answer(params SmtLiveDocument[] without)
    {
        var values = new Dictionary<string, object?>();
        foreach (var document in SmtLiveDocuments.RUN.Except(without))
            values[document.Variable] = (Guid?)m_server.AddFile(Path.Combine(m_documents, document.File));

        m_server.Jobs = (_, _) => SmtLiveJobFake.Completed(values);
    }

    #endregion
}
