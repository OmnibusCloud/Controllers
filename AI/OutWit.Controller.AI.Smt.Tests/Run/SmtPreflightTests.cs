using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Run;

/// <summary>
/// SmtPreflight through the engine: a set with taken and refused rows counted as a run reads it,
/// the options checked by name (a preflight says what is wrong rather than failing), the budget as
/// numbers, the upper bound "problems times the hard cap", the memory of a session and the packages
/// a run would make. No node is asked anything.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SmtPreflightTests : SmtRunTestBase
{
    #region Constants

    private const string LICENSE = "https://creativecommons.org/licenses/by/4.0/";

    #endregion

    #region Tests

    [Test]
    public async Task ASetWithRefusalsIsCountedAsARunReadsItTest()
    {
        var preflight = await PreflightAsync(Set(), new SmtOptionsData { HardCapSeconds = 10, SamplingRate = 0.5 });

        Assert.Multiple(() =>
        {
            Assert.That(preflight.Runnable, Is.True);
            Assert.That(preflight.Rows, Is.EqualTo(5));
            Assert.That(preflight.Accepted, Is.EqualTo(3));
            Assert.That(preflight.Refused, Is.EqualTo(2));
            Assert.That(preflight.Sha256, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(preflight.RefusalCodes.Select(count => (count.Name, count.Count)), Is.EquivalentTo(new[] { ("command_not_allowed", 1), ("field_missing", 1) }));
            Assert.That(preflight.Refusals.Select(refusal => refusal.Row), Is.EquivalentTo(new[] { 4, 5 }), "the refusals in full, by the line of the set");
            Assert.That(preflight.Refusals.Select(refusal => refusal.Reason), Has.All.Not.Contain("declare-const"), "a refusal never carries the text");
            Assert.That(preflight.Queries.Select(count => (count.Name, count.Count)), Is.EquivalentTo(new[] { ("check", 1), ("enumerate", 1), ("prove", 1) }));
            Assert.That(preflight.Expected.Single(count => count.Name == "sat").Count, Is.EqualTo(1));
            Assert.That(preflight.Logics.Single(count => count.Name == "QF_LIA").Count, Is.EqualTo(3));
            Assert.That(preflight.Stripped.Single(count => count.Name == "check-sat").Count, Is.EqualTo(2));
            Assert.That(preflight.Licenses.Single(count => count.Name == LICENSE).Count, Is.EqualTo(1));
            Assert.That(preflight.OptionProblems, Is.Empty);

            Assert.That(preflight.Profile, Is.EqualTo("z3-5.4.1"));
            Assert.That(preflight.ResourceLimit, Is.EqualTo(150_000_000), "the profile's number, never the word default");
            Assert.That(preflight.HardCapSeconds, Is.EqualTo(10));
            Assert.That(preflight.Fuel, Is.EqualTo(10 * 5_000_000_000L));
            Assert.That(preflight.MemoryMegabytes, Is.EqualTo(SmtOptionsRules.DEFAULT_MEMORY_MEGABYTES));
            Assert.That(preflight.MaxModels, Is.EqualTo(SmtOptionsRules.DEFAULT_MAX_MODELS));
            Assert.That(preflight.SamplingRate, Is.EqualTo(0.5));
            Assert.That(preflight.SessionMemoryBytes, Is.EqualTo(SmtNodeRig.SessionBytes(new SmtOptionsData { HardCapSeconds = 10 })));
            Assert.That(preflight.UpperBoundSeconds, Is.EqualTo(3 * 10), "the taken problems times the hard cap");
            Assert.That(preflight.Packages, Is.EqualTo(1));
            Assert.That(preflight.ProblemsPerPackage, Is.EqualTo(SmtRunRig.ProblemsPerPackage(3)));
            Assert.That(preflight.Messages, Has.Some.Contains("a bound, not an estimate"));
            Assert.That(string.Join(" ", preflight.Messages), Does.Not.Match("minute|hour|estimated time|ETA"), "no duration anywhere");
        });
    }

    [Test]
    public async Task OptionsOutOfRangeAreSaidByNameAndTheSetIsStillReadTest()
    {
        var preflight = await PreflightAsync(Set(), new SmtOptionsData { HardCapSeconds = 1_000, Profile = "cvc5-1.2", SamplingRate = 2 });

        Assert.Multiple(() =>
        {
            Assert.That(preflight.Runnable, Is.False);
            Assert.That(preflight.OptionProblems, Has.Some.Contains("'hard_cap_s'"));
            Assert.That(preflight.OptionProblems, Has.Some.Contains("'sampling_rate'"));
            Assert.That(preflight.OptionProblems, Has.Some.Contains("'profile'"));
            Assert.That(preflight.Accepted, Is.EqualTo(3), "the set is read with each such option at its default");
            Assert.That(preflight.HardCapSeconds, Is.EqualTo(SmtOptionsRules.DEFAULT_HARD_CAP_SECONDS));
        });
    }

    [Test]
    public async Task AnEmptySetWouldNotRunTest()
    {
        var preflight = await PreflightAsync("\n\n", new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(preflight.Runnable, Is.False);
            Assert.That(preflight.Rows, Is.Zero);
            Assert.That(preflight.Packages, Is.Zero);
            Assert.That(preflight.Messages, Has.Some.Contains("no row"));
        });
    }

    #endregion

    #region Tools

    private static string Set()
    {
        static string Row(Dictionary<string, object> fields)
        {
            return JsonSerializer.Serialize(fields);
        }

        return SmtSets.Of([
            Row(new() { ["id"] = "check", ["smt2"] = $"(set-logic QF_LIA)(set-info :status sat)(set-info :license \"{LICENSE}\")(declare-const x Int)(assert (> x 1))(check-sat)(get-model)" }),
            Row(new() { ["id"] = "enumerate", ["query"] = "enumerate", ["smt2"] = "(set-logic QF_LIA)(declare-const x Int)(assert (> x 1))(check-sat)", ["project"] = new[] { "x" } }),
            Row(new() { ["id"] = "prove", ["query"] = "prove", ["smt2"] = "(set-logic QF_LIA)(declare-const x Int)(assert (> x 1))", ["goal"] = "(> x 0)" }),
            Row(new() { ["id"] = "set-option", ["smt2"] = "(set-option :produce-models true)(declare-const x Int)" }),
            Row(new() { ["id"] = "no-text" })
        ]);
    }

    private async Task<SmtPreflightData> PreflightAsync(string set, SmtOptionsData options)
    {
        var job = Engine.Compile(Script("SmtPreflight.wit"));
        var status = await Engine.ScheduleAndWaitAsync(job, await UploadAsync(set, "problems.jsonl"), options);
        Assert.That(status.Result, Is.EqualTo(OutWit.Engine.Interfaces.WitProcessingResult.Completed), status.ToString());

        return (SmtPreflightData)job.Variables["result"].Value!;
    }

    #endregion
}
