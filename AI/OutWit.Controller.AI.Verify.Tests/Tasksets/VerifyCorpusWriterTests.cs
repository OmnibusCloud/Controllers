using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// The corpus: one JSON line per task in index order, outputs verbatim, truncation flagged only
/// when it happened.
/// </summary>
[TestFixture]
public sealed class VerifyCorpusWriterTests
{
    [Test]
    public void OneLinePerTaskInIndexOrderTest()
    {
        var results = new List<VerifyResultData>
        {
            new() { TaskIndex = 1, Verdict = VerifyVerdict.Fail, ExitCode = 1, Stdout = "b\n", Stderr = "warn\n" },
            new() { TaskIndex = 0, Verdict = VerifyVerdict.Pass, Stdout = "a \"quoted\"\n" }
        };

        var lines = Encoding.UTF8.GetString(VerifyCorpusWriter.Write(results)).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.That(lines, Has.Length.EqualTo(2));
        var first = JsonDocument.Parse(lines[0]).RootElement;
        var second = JsonDocument.Parse(lines[1]).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(first.GetProperty("index").GetInt32(), Is.Zero);
            Assert.That(first.GetProperty("verdict").GetString(), Is.EqualTo("Pass"));
            Assert.That(first.GetProperty("stdout").GetString(), Is.EqualTo("a \"quoted\"\n"));
            Assert.That(first.TryGetProperty("stdout_truncated", out _), Is.False);
            Assert.That(second.GetProperty("index").GetInt32(), Is.EqualTo(1));
            Assert.That(second.GetProperty("exit").GetInt32(), Is.EqualTo(1));
            Assert.That(second.GetProperty("stderr").GetString(), Is.EqualTo("warn\n"));
        });
    }

    [Test]
    public void TruncationIsFlaggedTest()
    {
        var results = new List<VerifyResultData>
        {
            new() { TaskIndex = 0, Verdict = VerifyVerdict.OutputExceeded, Stdout = "x", StdoutTruncated = true, StderrTruncated = true }
        };

        var line = Encoding.UTF8.GetString(VerifyCorpusWriter.Write(results)).TrimEnd('\n');
        var root = JsonDocument.Parse(line).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("stdout_truncated").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("stderr_truncated").GetBoolean(), Is.True);
        });
    }

    [Test]
    public void OutputsAreNotHtmlEscapedTest()
    {
        var results = new List<VerifyResultData> { new() { TaskIndex = 0, Verdict = VerifyVerdict.Pass, Stdout = "2 + 2 < 5 & true é\n" } };

        var line = Encoding.UTF8.GetString(VerifyCorpusWriter.Write(results));

        Assert.That(line, Does.Contain("2 + 2 < 5 & true é"));
    }

    [Test]
    public void ATaskWithASuiteCarriesEveryCaseTest()
    {
        var results = new List<VerifyResultData>
        {
            new()
            {
                TaskIndex = 0,
                Verdict = VerifyVerdict.Fail,
                ExitCode = 0,
                Stderr = "warned\n",
                CaseResults =
                [
                    new VerifyCaseResultData { CaseIndex = 0, Passed = true, Verdict = VerifyVerdict.Pass, ActualStdout = "5\n" },
                    new VerifyCaseResultData { CaseIndex = 1, Passed = false, Verdict = VerifyVerdict.Fail, ExitCode = 3, ActualStdout = "6\n" }
                ]
            },
            new() { TaskIndex = 1, Verdict = VerifyVerdict.Pass, Stdout = "plain\n" }
        };

        var lines = Encoding.UTF8.GetString(VerifyCorpusWriter.Write(results)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var suite = JsonDocument.Parse(lines[0]).RootElement;
        var cases = suite.GetProperty("cases").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(cases, Has.Count.EqualTo(2));
            Assert.That(cases[0].GetProperty("passed").GetBoolean(), Is.True);
            Assert.That(cases[0].GetProperty("stdout").GetString(), Is.EqualTo("5\n"));
            Assert.That(cases[1].GetProperty("case").GetInt32(), Is.EqualTo(1));
            Assert.That(cases[1].GetProperty("verdict").GetString(), Is.EqualTo("Fail"));
            Assert.That(cases[1].GetProperty("exit").GetInt32(), Is.EqualTo(3));
            Assert.That(suite.GetProperty("stderr").GetString(), Is.EqualTo("warned\n"));
            Assert.That(JsonDocument.Parse(lines[1]).RootElement.TryGetProperty("cases", out _), Is.False, "a task without a suite has no cases");
        });
    }

    [Test]
    public void EmptyRunIsAnEmptyCorpusTest()
    {
        Assert.That(VerifyCorpusWriter.Write([]), Is.Empty);
    }
}
