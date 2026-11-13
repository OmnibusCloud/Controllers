using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Tasksets;
using OutWit.Controller.AI.Verify.Tests.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Demo;

/// <summary>
/// The demo kits of the live tool (demo/): every task of a kit gets, in the sandbox here, the
/// verdict the kit's table says it gets on the fleet. A kit that drifts from its EXPECTED.md
/// fails here first.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyDemoKitTests : VerifySandboxTestBase
{
    #region Constants

    private static readonly string DEMO = Path.Combine("OutWit.Controller.AI.Verify.Live", "demo");

    #endregion

    #region Kit Tests

    [Test]
    public void TheRlvrKitGivesEveryCandidateItsVerdictTest()
    {
        var kit = Path.Combine(DemoRoot(), "01-rlvr");
        var parsed = VerifyTasksetParser.Parse(File.ReadAllText(Path.Combine(kit, "taskset.jsonl")));
        Assert.That(parsed.Errors, Is.Empty);

        var expected = File.ReadAllLines(Path.Combine(kit, "candidates.csv"))
            .Skip(1)
            .Select(line => line.Split(','))
            .ToDictionary(fields => int.Parse(fields[0]), fields => (Name: $"{fields[1]}/{fields[2]}", Verdict: Enum.Parse<VerifyVerdict>(fields[4])));

        var wrong = new List<string>();
        foreach (var task in parsed.Tasks)
        {
            var result = Run(RuntimeOf(task), task);
            var (name, verdict) = expected[task.TaskIndex];
            if (result.Verdict != verdict)
                wrong.Add($"{task.TaskIndex} {name}: {result.Verdict}, expected {verdict}; {result.Stderr.Split('\n')[0]}");
        }

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks, Has.Count.EqualTo(expected.Count));
            Assert.That(wrong, Is.Empty, string.Join("\n", wrong));
        });
    }

    #endregion

    #region Tools

    private VerifyResolvedRuntime RuntimeOf(VerifyTaskData task)
    {
        var runtime = VerifyRuntimeCatalog.Resolve(RuntimesRoot, task.RuntimeId, out var reason);
        Assert.That(runtime, Is.Not.Null, reason);
        return runtime!;
    }

    private string DemoRoot()
    {
        return Path.GetFullPath(Path.Combine(RuntimesRoot, "..", "..", "..", DEMO));
    }

    #endregion
}
