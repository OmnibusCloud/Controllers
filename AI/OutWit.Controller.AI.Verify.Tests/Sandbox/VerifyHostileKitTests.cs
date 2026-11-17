using System.Diagnostics;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The hostile taskset of the fleet proofs as a thing of the repository: the committed files
/// are what the generator writes (<see cref="VerifyHostileKit"/>), the taskset is one the
/// schema allows, and run through the sandbox here every task gets the verdict the fleet must
/// give it. The kit drifting from its expectations, or the sandbox from the kit, fails here
/// before a fleet is asked.
/// <para>
/// To write the files again after a change of the generator:
/// <c>WITAI_VERIFY_WRITE_HOSTILE_KIT=1 dotnet test --filter FullyQualifiedName~VerifyHostileKitTests</c>.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyHostileKitTests : VerifySandboxTestBase
{
    #region Constants

    private const string WRITE_ENV = "WITAI_VERIFY_WRITE_HOSTILE_KIT";
    private const int TASKS = 15;
    private const int SLEEP_WALL_MS = 30_000;

    // The file numbers follow from the guest path alone. These are the three a linux-x64 and a
    // windows-x64 node of the test fleet returned (2.4.5-test.18, 2026-11-17); the build server's
    // three platforms hold the sandbox to the same ones here.
    private const string FILE_NUMBERS =
        "/lib/python314.zip 6886437772809089997\n/lib 14382849713876679005\n/ 12638123428881205758\n";

    private static readonly string TASKSETS = Path.Combine("OutWit.Controller.AI.Verify.Live", "tasksets");

    #endregion

    #region Tests

    [Test]
    public void TheCommittedKitIsWhatTheGeneratorWritesTest()
    {
        var (taskset, expected) = VerifyHostileKit.Write();

        if (Environment.GetEnvironmentVariable(WRITE_ENV) == "1")
        {
            File.WriteAllText(KitFile(VerifyHostileKit.TASKSET_FILE), taskset);
            File.WriteAllText(KitFile(VerifyHostileKit.EXPECTED_FILE), expected);
            TestContext.Out.WriteLine($"wrote {VerifyHostileKit.TASKSET_FILE} and {VerifyHostileKit.EXPECTED_FILE}");
        }

        Assert.Multiple(() =>
        {
            // A checkout may give the files the line ends of its system; the content is the same.
            Assert.That(Lines(File.ReadAllText(KitFile(VerifyHostileKit.TASKSET_FILE))), Is.EqualTo(Lines(taskset)), "the taskset is what the generator writes, to the byte");
            Assert.That(Lines(File.ReadAllText(KitFile(VerifyHostileKit.EXPECTED_FILE))), Is.EqualTo(Lines(expected)), "the expected verdicts are what the generator writes");
            Assert.That(Lines(taskset), Has.Length.EqualTo(TASKS));
        });
    }

    [Test]
    public void TheKitIsATasksetTheSchemaAllowsTest()
    {
        var parsed = VerifyTasksetParser.Parse(File.ReadAllText(KitFile(VerifyHostileKit.TASKSET_FILE)));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Errors, Is.Empty);
            Assert.That(parsed.Tasks.Select(task => task.TaskIndex), Is.EqualTo(Enumerable.Range(0, TASKS)));
            Assert.That(VerifyTasksetChecks.UnknownRuntimes(parsed.Tasks), Is.Empty);
        });
    }

    [Test]
    public void TheKitHoldsItsExpectedVerdictsInTheLocalSandboxTest()
    {
        var expected = File.ReadAllLines(KitFile(VerifyHostileKit.EXPECTED_FILE))
            .Where(line => line.Length > 0)
            .Select(line => line.Split('\t'))
            .ToDictionary(fields => int.Parse(fields[0]), fields => (Verdict: Enum.Parse<VerifyVerdict>(fields[1]), What: fields[2]));

        // As Split plans it and a node runs it: the taskset parsed, cut into batches by runtime,
        // every batch executed with the limits a node holds it to.
        var parsed = VerifyTasksetParser.Parse(File.ReadAllText(KitFile(VerifyHostileKit.TASKSET_FILE)));
        Assert.That(parsed.Errors, Is.Empty);
        var plan = VerifyTasksetPlanner.Plan(parsed.Tasks, new VerifyOptionsData { BatchSize = 2 });
        Assert.That(plan.Errors, Is.Empty);

        var clock = Stopwatch.StartNew();
        var results = plan.Batches
            .SelectMany(batch => VerifyBatchExecutor.Execute(Sandbox, RuntimesRoot, batch).Results)
            .ToDictionary(result => result.TaskIndex);
        clock.Stop();

        var wrong = new List<string>();
        foreach (var (index, (verdict, what)) in expected.OrderBy(item => item.Key))
        {
            var result = results[index];
            TestContext.Out.WriteLine($"{index,2} {result.Verdict,-14} fuel {result.FuelConsumed,14:N0} wall {result.WallMs,6} ms  {what}");
            if (result.Verdict != verdict)
                wrong.Add($"{index} {what}: {result.Verdict}, expected {verdict}; {result.Stderr.Split('\n')[0]}");
        }

        TestContext.Out.WriteLine($"the kit took {clock.Elapsed.TotalSeconds:F1} s here");

        Assert.Multiple(() =>
        {
            Assert.That(expected, Has.Count.EqualTo(TASKS));
            Assert.That(results, Has.Count.EqualTo(TASKS));
            Assert.That(wrong, Is.Empty, string.Join("\n", wrong));

            // No task of the kit ends by a node's clock: every one has a verdict, none a fault.
            Assert.That(results.Values.Where(result => !string.IsNullOrEmpty(result.Fault)).Select(result => result.TaskIndex), Is.Empty,
                "a task without a verdict would be a node fault, which the kit does not reach");

            // The sleeper asked for a hundred seconds and took none of the node's.
            Assert.That(results[VerifyHostileKit.SLEEP_TASK].WallMs, Is.LessThan(SLEEP_WALL_MS));

            // The fill is ended by its fuel, at the budget to the unit.
            Assert.That(results[VerifyHostileKit.FILL_TASK].FuelConsumed, Is.EqualTo(VerifySandboxDefaults.DEFAULT_FUEL_BUDGET));

            // What a program reads about a file is the same wherever it runs.
            Assert.That(results[VerifyHostileKit.FILE_RECORDS_TASK].Stdout, Is.EqualTo("/lib/python314.zip 0 0 0 0 1 True\n/lib 0 0 0 0 1 True\n/ 0 0 0 0 1 True\n"));
            Assert.That(results[VerifyHostileKit.FILE_NUMBERS_TASK].CaseResults.Select(item => item.ActualStdout), Is.EqualTo(new[] { FILE_NUMBERS }),
                "the file numbers a program reads follow from the guest path alone");
        });
    }

    #endregion

    #region Tools

    private string KitFile(string name)
    {
        return Path.GetFullPath(Path.Combine(RuntimesRoot, "..", "..", "..", TASKSETS, name));
    }

    private static string[] Lines(string text)
    {
        return text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToArray();
    }

    #endregion
}
