using System.Text;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>The result that stands for a task is chosen by rule, never by arrival order.</summary>
[TestFixture]
public sealed class VerifyExecutionsTests
{
    private static VerifyResultData Result(int index, VerifyVerdict verdict = VerifyVerdict.Pass, string stdout = "", int exit = 0)
    {
        return new VerifyResultData { TaskIndex = index, Verdict = verdict, ExitCode = exit, Stdout = stdout };
    }

    [Test]
    public void OneResultPerTaskInTaskOrderTest()
    {
        var canonical = VerifyExecutions.Canonical([Result(2), Result(0), Result(2), Result(1)]);

        Assert.That(canonical.Select(result => result.TaskIndex), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void TheChoiceDoesNotDependOnArrivalTest()
    {
        var pass = Result(0, VerifyVerdict.Pass, "4\n");
        var fail = Result(0, VerifyVerdict.Fail, "5\n", exit: 1);

        Assert.That(VerifyExecutions.Pick([pass, fail]), Is.SameAs(pass));
        Assert.That(VerifyExecutions.Pick([fail, pass]), Is.SameAs(pass));
    }

    [Test]
    public void OutputsBreakATieTest()
    {
        var low = Result(0, stdout: "a");
        var high = Result(0, stdout: "b");

        Assert.That(VerifyExecutions.Pick([high, low]), Is.SameAs(low));
    }

    [Test]
    public void TheReportAndTheCorpusCountATaskExecutedTwiceOnceTest()
    {
        var report = VerifyReportWriter.ReadSummary(VerifyReportWriter.Write([Result(0), Result(0), Result(1, VerifyVerdict.Fail)]));
        var corpus = Encoding.UTF8.GetString(VerifyCorpusWriter.Write([Result(0), Result(0), Result(1, VerifyVerdict.Fail)]));

        Assert.Multiple(() =>
        {
            Assert.That(report.Total, Is.EqualTo(2));
            Assert.That(corpus.Split('\n', StringSplitOptions.RemoveEmptyEntries), Has.Length.EqualTo(2));
        });
    }
}
