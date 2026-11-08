using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// The taskset parser at its edges: every malformed shape is one collected error naming its
/// line, never an exception; indices are unique and non-negative; editors' habits (a BOM,
/// CRLF) do not count as errors; limits below one fall back to the defaults.
/// </summary>
[TestFixture]
public sealed class VerifyTasksetParserTests
{
    private const string GOOD = """{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"}}""";

    [Test]
    public void MissingFieldsAreNamedPerLineTest()
    {
        var parsed = VerifyTasksetParser.Parse(string.Join("\n",
            """{"sources":{"m.py":"print(1)"}}""",
            """{"runtime":"python-3.14.6"}""",
            """{"runtime":"python-3.14.6","sources":{}}""",
            """{"runtime":"python-3.14.6","entry":"other.py","sources":{"m.py":"print(1)"}}""",
            "not json at all"));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks, Is.Empty);
            Assert.That(parsed.Errors, Has.Count.EqualTo(5));
            Assert.That(parsed.Errors[0], Does.StartWith("line 1:").And.Contain("'runtime'"));
            Assert.That(parsed.Errors[1], Does.StartWith("line 2:").And.Contain("'sources'"));
            Assert.That(parsed.Errors[2], Does.StartWith("line 3:").And.Contain("'sources'"));
            Assert.That(parsed.Errors[3], Does.StartWith("line 4:").And.Contain("'entry'"));
            Assert.That(parsed.Errors[4], Does.StartWith("line 5:"));
        });
    }

    [Test]
    public void NonStringSourceAndNonObjectCaseAreErrorsTest()
    {
        var parsed = VerifyTasksetParser.Parse(string.Join("\n",
            """{"runtime":"python-3.14.6","sources":{"m.py":42}}""",
            """{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"},"suite":[1]}"""));

        Assert.That(parsed.Tasks, Is.Empty);
        Assert.That(parsed.Errors.Select(e => e[..7]), Is.EqualTo(new[] { "line 1:", "line 2:" }));
    }

    [Test]
    public void IndicesDefaultToTheOrdinalOfParsedLinesTest()
    {
        var parsed = VerifyTasksetParser.Parse(string.Join("\n", GOOD, "", "broken", GOOD, GOOD));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks.Select(t => t.TaskIndex), Is.EqualTo(new[] { 0, 1, 2 }), "blank and malformed lines do not consume an index");
            Assert.That(parsed.Errors, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void NegativeAndDuplicateIndicesAreErrorsTest()
    {
        var parsed = VerifyTasksetParser.Parse(string.Join("\n",
            """{"index":-1,"runtime":"python-3.14.6","sources":{"m.py":"print(1)"}}""",
            """{"index":5,"runtime":"python-3.14.6","sources":{"m.py":"print(1)"}}""",
            """{"index":5,"runtime":"quickjs-0.15.1","sources":{"m.js":"1"}}"""));

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Tasks.Select(t => t.TaskIndex), Is.EqualTo(new[] { 5 }));
            Assert.That(parsed.Errors, Has.Count.EqualTo(2));
            Assert.That(parsed.Errors[0], Does.StartWith("line 1:").And.Contain("0 or more"));
            Assert.That(parsed.Errors[1], Does.StartWith("line 3:").And.Contain("duplicate").And.Contain("line 2"));
        });
    }

    [Test]
    public void BomAndCrlfAreNotErrorsTest()
    {
        var parsed = VerifyTasksetParser.Parse("﻿" + GOOD + "\r\n" + GOOD + "\r\n");

        Assert.That(parsed.Errors, Is.Empty);
        Assert.That(parsed.Tasks, Has.Count.EqualTo(2));
    }

    [Test]
    public void LimitsBelowOneFallBackToTheDefaultsTest()
    {
        var parsed = VerifyTasksetParser.Parse("""{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"},"limits":{"fuel":-5,"memory":0,"wall_ms":2500}}""");
        var limits = VerifySandboxDefaultsFor(parsed.Tasks[0]);

        Assert.Multiple(() =>
        {
            Assert.That(limits.FuelBudget, Is.EqualTo(OutWit.Controller.AI.Verify.Sandbox.VerifySandboxDefaults.DEFAULT_FUEL_BUDGET));
            Assert.That(limits.MemoryBytes, Is.EqualTo(OutWit.Controller.AI.Verify.Sandbox.VerifySandboxDefaults.DEFAULT_MEMORY_BYTES));
            Assert.That(limits.WallTimeMs, Is.EqualTo(2500));
        });
    }

    [Test]
    public void ALargeSourceParsesTest()
    {
        var source = new string('#', 2 * 1024 * 1024);
        var parsed = VerifyTasksetParser.Parse($$$"""{"runtime":"python-3.14.6","sources":{"m.py":"{{{source}}}"}}""");

        Assert.That(parsed.Errors, Is.Empty);
        Assert.That(parsed.Tasks[0].Sources[0].Content, Has.Length.EqualTo(source.Length));
    }

    [Test]
    public void OptionalFieldsOfTheWrongTypeAreIgnoredTest()
    {
        var parsed = VerifyTasksetParser.Parse("""{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"},"stdin":7,"args":"x","seed":"s","suite":{},"limits":[]}""");

        Assert.That(parsed.Errors, Is.Empty);
        var task = parsed.Tasks[0];
        Assert.Multiple(() =>
        {
            Assert.That(task.Stdin, Is.Null);
            Assert.That(task.Args, Is.Empty);
            Assert.That(task.RandomSeed, Is.Zero);
            Assert.That(task.Suite, Is.Null);
            Assert.That(task.Limits, Is.Null);
        });
    }

    private static VerifyLimitsData VerifySandboxDefaultsFor(VerifyTaskData task)
    {
        return OutWit.Controller.AI.Verify.Sandbox.VerifySandboxDefaults.Resolve(task.Limits, null);
    }
}
