using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// The checker spec: a task line plus the verdict rule; a malformed line is an error, a missing
/// rule means "exit code 0".
/// </summary>
[TestFixture]
public sealed class VerifyCheckerSpecTests
{
    [Test]
    public void ParsesTheTaskVocabularyAndTheVerdictRuleTest()
    {
        const string json = """{"runtime":"python-3.14.6","entry":"check.py","sources":{"check.py":"print('OK')"},"args":["-q"],"expected_stdout":"OK\n","expected_exit":3,"limits":{"wall_ms":2000}}""";

        var spec = VerifyCheckerSpec.Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(spec.RuntimeId, Is.EqualTo("python-3.14.6"));
            Assert.That(spec.EntryPoint, Is.EqualTo("check.py"));
            Assert.That(spec.Sources.Single().Content, Is.EqualTo("print('OK')"));
            Assert.That(spec.Args, Is.EqualTo(new[] { "-q" }));
            Assert.That(spec.ExpectedStdout, Is.EqualTo("OK\n"));
            Assert.That(spec.ExpectedExitCode, Is.EqualTo(3));
            Assert.That(spec.Limits?.WallTimeMs, Is.EqualTo(2000));
        });
    }

    [Test]
    public void MissingRuleMeansExitCodeZeroOnlyTest()
    {
        var spec = VerifyCheckerSpec.Parse("""{"runtime":"quickjs-0.15.1","entry":"c.js","sources":{"c.js":"1"}}""");

        Assert.Multiple(() =>
        {
            Assert.That(spec.ExpectedStdout, Is.Null);
            Assert.That(spec.ExpectedExitCode, Is.Zero);
            Assert.That(spec.Limits, Is.Null);
        });
    }

    [Test]
    public void MalformedSpecIsAFormatErrorTest()
    {
        Assert.Throws<FormatException>(() => VerifyCheckerSpec.Parse("""{"entry":"c.py"}"""));
    }
}
