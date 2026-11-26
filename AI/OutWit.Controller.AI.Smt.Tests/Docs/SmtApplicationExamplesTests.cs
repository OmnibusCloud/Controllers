using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Controller.AI.Smt.Tests.Run;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Docs;

/// <summary>
/// The examples of the application page are written by the code, never typed: the example set of
/// the page is run through the engine on three in-process nodes (SmtPreflight and SmtRun, the
/// bundled scripts), and the page's preflight, results, report and integrity document are held to
/// what the run gives, its capabilities document to what the controller writes. With the variable
/// <see cref="WRITE_VARIABLE"/> set to 1 the test writes the examples into the page instead, after
/// a change of the code that changes them. What the page says of a job's failures and of the
/// options a request document can carry (null, the defaults) is held here too, through the engine.
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtApplicationExamplesTests : SmtRunTestBase
{
    #region Constants

    /// <summary>The variable that has the test write the examples into the page.</summary>
    public const string WRITE_VARIABLE = "WITAI_SMT_WRITE_EXAMPLES";

    private const string APPLICATION = "AI/OutWit.Controller.AI.Smt/docs/application.md";

    private static readonly string[] EXAMPLES = ["preflight", "capabilities", "results", "report", "integrity"];

    #endregion

    #region Example Tests

    [Test]
    public async Task TheExamplesOfThePageAreWhatTheCodeWritesTest()
    {
        var page = SmtMarkdown.Read(APPLICATION);
        var set = SmtMarkdown.Block(page, "set") + "\n";
        var options = new SmtOptionsData { SamplingRate = 1 };

        var preflight = await PreflightAsync(set, options);
        var run = await RunAsync(set, options);
        var written = new Dictionary<string, string>
        {
            ["preflight"] = SmtExamplePrinter.Model(preflight),
            ["capabilities"] = SmtExamplePrinter.Document(Encoding.UTF8.GetString(SmtDiagnosticsRig.Capabilities(new SmtRuntimeDiagnosticsData { ModulePresent = true }))),
            ["results"] = run.ResultsText.TrimEnd('\n'),
            ["report"] = SmtExamplePrinter.Document(run.ReportText),
            ["integrity"] = SmtExamplePrinter.Document(run.IntegrityText)
        };

        if (Environment.GetEnvironmentVariable(WRITE_VARIABLE) == "1")
        {
            var text = EXAMPLES.Aggregate(page, (current, name) => SmtMarkdown.Replace(current, name, written[name]));
            File.WriteAllText(SmtMarkdown.PathOf(APPLICATION), text, new UTF8Encoding(false));
            Assert.Pass($"the examples were written into {APPLICATION}");
        }

        Assert.Multiple(() =>
        {
            foreach (var name in EXAMPLES)
                Assert.That(SmtMarkdown.Block(page, name), Is.EqualTo(written[name]), $"the example '{name}' of {APPLICATION} is not what the code writes: run this test with {WRITE_VARIABLE}=1 to write it");
        });
    }

    [Test]
    public async Task WhatThePageSaysOfTheExampleRunHoldsTest()
    {
        var set = SmtMarkdown.Block(SmtMarkdown.Read(APPLICATION), "set") + "\n";
        var options = new SmtOptionsData { SamplingRate = 1 };

        var preflight = await PreflightAsync(set, options);
        var run = await RunAsync(set, options);
        var taken = run.Rows.Where(row => row.GetProperty("status").GetString() != SmtRunWords.REFUSED).ToList();

        Assert.Multiple(() =>
        {
            Assert.That((preflight.Rows, preflight.Accepted, preflight.Refused), Is.EqualTo((5, 4, 1)), "a check with values, a check with no model, an enumeration, a proof, and a refused row");
            Assert.That(run.Row(3).GetProperty("id").GetString(), Is.EqualTo("double"), "the row after the blank line has the index 3");
            Assert.That(preflight.Refusals.Single().Row, Is.EqualTo(6), "and a refusal names the line of the set, blank lines counted");
            Assert.That(run.Row(4).GetProperty("code").GetString(), Is.EqualTo("command_not_allowed"));
            Assert.That(run.Row(0).GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.VALIDATED), "the check's model and its values are checked");
            Assert.That(run.Row(1).GetProperty("verdict").GetString(), Is.EqualTo(SmtResultWords.UNSAT));
            Assert.That(run.Row(2).GetProperty("count").GetInt32(), Is.EqualTo(3), "the enumeration finds every model");
            Assert.That(run.Row(3).GetProperty("proof").GetString(), Is.EqualTo(SmtResultWords.PROVED));
            Assert.That(taken.Select(row => row.GetProperty("settled_by").GetString()), Has.All.EqualTo(SmtRunWords.COPIES), "every package on two machines");
            Assert.That(run.Report.GetProperty("run").GetProperty("binding").GetString(), Is.EqualTo(SmtRunWords.BOUND));
        });

        SmtRunChecks.AssertSchemas(run);
        SmtRunChecks.AssertNamesNoMachine(run);
    }

    #endregion

    #region Job Tests

    [Test]
    public async Task NullOptionsAreTheDefaultsTest()
    {
        // The options a request document carries for SmtRun: null.
        var set = SmtMarkdown.Block(SmtMarkdown.Read(APPLICATION), "set") + "\n";
        var defaults = SmtOptionsRules.Resolve(new SmtOptionsData());

        var preflight = await PreflightAsync(set, null);

        var job = Engine.Compile(Script("SmtRun.wit"));
        var status = await Engine.ScheduleAndWaitAsync(job, await UploadAsync(set, "problems.jsonl"), null!);
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());
        var run = await SmtRunDocuments.ReadAsync(job, Blobs);
        var used = run.Report.GetProperty("run").GetProperty("options");

        Assert.Multiple(() =>
        {
            Assert.That(preflight.Runnable, Is.True);
            Assert.That(preflight.OptionProblems, Is.Empty);
            Assert.That((preflight.HardCapSeconds, preflight.MemoryMegabytes, preflight.MaxModels, preflight.SamplingRate), Is.EqualTo((defaults.HardCapSeconds, defaults.MemoryMegabytes, defaults.MaxModels, defaults.SamplingRate)));
            Assert.That(used.GetProperty("hard_cap_s").GetInt32(), Is.EqualTo(defaults.HardCapSeconds));
            Assert.That(used.GetProperty("sampling_rate").GetDouble(), Is.EqualTo(SmtOptionsRules.DEFAULT_SAMPLING_RATE));
            Assert.That(used.GetProperty("rlimit").GetInt64(), Is.EqualTo(preflight.ResourceLimit), "the profile's resource limit, as a number");
        });
    }

    [Test]
    public async Task AJobThatCannotStartFailsWithThePagesSentencesTest()
    {
        var page = SmtMarkdown.Read(APPLICATION);
        var set = SmtMarkdown.Block(page, "set") + "\n";
        var ends = SmtMarkdown.Table(page, "### How a job ends");

        var empty = await FailedAsync("\n\n", new SmtOptionsData());
        var range = await FailedAsync(set, new SmtOptionsData { HardCapSeconds = 1_000 });
        var profile = await FailedAsync(set, new SmtOptionsData { Profile = "another" });

        var job = Engine.Compile(Script("SmtRun.wit"));
        var refused = await Engine.ScheduleAndWaitAsync(job, await UploadAsync("{\"id\": \"push\", \"smt2\": \"(push 1)\"}\n", "problems.jsonl"), new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(empty, Does.Contain(SmtMarkdown.Codes(ends.Row("the set has no row")[1]).Single()));
            Assert.That(range, Does.Contain(SmtMarkdown.Codes(ends.Row("an option is out of range")[1]).First()));
            Assert.That(profile, Does.Contain(SmtMarkdown.Codes(ends.Row("the profile is not the controller's")[1]).Single()));
            Assert.That(refused.Result, Is.EqualTo(WitProcessingResult.Completed), "a set whose every row is refused is no failure");
        });
    }

    #endregion

    #region Tools

    private async Task<SmtPreflightData> PreflightAsync(string set, SmtOptionsData? options)
    {
        var job = Engine.Compile(Script("SmtPreflight.wit"));
        var status = await Engine.ScheduleAndWaitAsync(job, await UploadAsync(set, "problems.jsonl"), options!);
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());

        return (SmtPreflightData)job.Variables["result"].Value!;
    }

    /// <summary>Runs SmtRun, which must fail, and gives every message of its status.</summary>
    private async Task<string> FailedAsync(string set, SmtOptionsData options)
    {
        var job = Engine.Compile(Script("SmtRun.wit"));
        var status = await Engine.ScheduleAndWaitAsync(job, await UploadAsync(set, "problems.jsonl"), options);
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed), status.ToString());

        return string.Join("\n", Messages(status));
    }

    private static IEnumerable<string> Messages(IWitProcessingStatus status)
    {
        return status.Children.SelectMany(Messages).Prepend(status.Message ?? "");
    }

    #endregion
}
