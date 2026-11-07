using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// Candidates → check tasks: one per passing candidate with output, under the candidate's index,
/// with the output on stdin and the spec's verdict rule as the suite; everything else is skipped
/// with a note.
/// </summary>
[TestFixture]
public sealed class VerifyCheckTaskBuilderTests
{
    private static readonly VerifyCheckerSpec CHECKER = new(
        "python-3.14.6", "check.py",
        [new VerifySourceFileData { Name = "check.py", Content = "import sys;print('OK')" }],
        ["--strict"], "OK\n", 0,
        new VerifyLimitsData { WallTimeMs = 3000 });

    [Test]
    public void PassingCandidatesBecomeChecksUnderTheirIndicesTest()
    {
        var candidates = new List<VerifyResultData>
        {
            new() { TaskIndex = 5, Verdict = VerifyVerdict.Pass, Stdout = "five\n" },
            new() { TaskIndex = 2, Verdict = VerifyVerdict.Pass, Stdout = "two\n" }
        };

        var plan = VerifyCheckTaskBuilder.Build(candidates, CHECKER);

        Assert.That(plan.Skipped, Is.Empty);
        Assert.That(plan.Tasks.Select(task => task.TaskIndex), Is.EqualTo(new[] { 2, 5 }));

        var check = plan.Tasks[0];
        Assert.Multiple(() =>
        {
            Assert.That(check.RuntimeId, Is.EqualTo("python-3.14.6"));
            Assert.That(check.EntryPoint, Is.EqualTo("check.py"));
            Assert.That(check.Sources.Single().Content, Is.EqualTo("import sys;print('OK')"));
            Assert.That(check.Args, Is.EqualTo(new[] { "--strict" }));
            Assert.That(check.Stdin, Is.EqualTo("two\n"));
            Assert.That(check.Limits?.WallTimeMs, Is.EqualTo(3000));
            Assert.That(check.Suite?.Cases.Single().Stdin, Is.EqualTo("two\n"));
            Assert.That(check.Suite?.Cases.Single().ExpectedStdout, Is.EqualTo("OK\n"));
            Assert.That(check.Suite?.Cases.Single().ExpectedExitCode, Is.Zero);
        });
    }

    [Test]
    public void CandidatesWithoutUsableOutputAreSkippedWithANoteTest()
    {
        var candidates = new List<VerifyResultData>
        {
            new() { TaskIndex = 0, Verdict = VerifyVerdict.RuntimeError, Stdout = "" },
            new() { TaskIndex = 1, Verdict = VerifyVerdict.Pass, Stdout = "" },
            new() { TaskIndex = 2, Verdict = VerifyVerdict.Pass, Stdout = "cut", StdoutTruncated = true },
            new() { TaskIndex = 3, Verdict = VerifyVerdict.Pass, Stdout = "ok\n" }
        };

        var plan = VerifyCheckTaskBuilder.Build(candidates, CHECKER);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks.Select(task => task.TaskIndex), Is.EqualTo(new[] { 3 }));
            Assert.That(plan.Skipped, Has.Count.EqualTo(3));
            Assert.That(plan.Skipped[0], Does.Contain("candidate 0").And.Contain("RuntimeError"));
            Assert.That(plan.Skipped[1], Does.Contain("empty output"));
            Assert.That(plan.Skipped[2], Does.Contain("truncated"));
        });
    }

    [Test]
    public void SourcesAreCopiedNotSharedTest()
    {
        var candidates = new List<VerifyResultData>
        {
            new() { TaskIndex = 0, Verdict = VerifyVerdict.Pass, Stdout = "a\n" },
            new() { TaskIndex = 1, Verdict = VerifyVerdict.Pass, Stdout = "b\n" }
        };

        var plan = VerifyCheckTaskBuilder.Build(candidates, CHECKER);

        Assert.That(plan.Tasks[0].Sources[0], Is.Not.SameAs(plan.Tasks[1].Sources[0]));
        Assert.That(plan.Tasks[0].Sources[0], Is.Not.SameAs(CHECKER.Sources[0]));
    }
}
