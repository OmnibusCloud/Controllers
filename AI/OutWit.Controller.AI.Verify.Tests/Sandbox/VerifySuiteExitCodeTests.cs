using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// Exit codes in a suite: the expected code is the pass condition whatever it is; a crash the
/// case did not expect is a RuntimeError; a wrong answer or a missing signal is a Fail. The
/// checker contract of generate-and-check rests on this.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifySuiteExitCodeTests : VerifySandboxTestBase
{
    [Test]
    public void ExpectedNonZeroExitPassesTest()
    {
        var suite = Suite(expectedExit: 3);
        var result = Run(Python, PythonTask("raise SystemExit(3)", suite: suite));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass));
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.CaseResults.Single().Passed, Is.True);
            Assert.That(result.CaseResults.Single().Verdict, Is.EqualTo(VerifyVerdict.Pass));
        });
    }

    [Test]
    public void UnexpectedCrashIsARuntimeErrorTest()
    {
        var suite = Suite(expectedExit: 0);
        var result = Run(Python, PythonTask("raise SystemExit(1)", suite: suite));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.CaseResults.Single().Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
        });
    }

    [Test]
    public void MissingExpectedSignalIsAFailTest()
    {
        var suite = Suite(expectedExit: 2);
        var result = Run(Python, PythonTask("print('fine')", suite: suite));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Fail));
            Assert.That(result.ExitCode, Is.Zero);
        });
    }

    [Test]
    public void CheckerStyleVerdictByOutputIsAFailOnMismatchTest()
    {
        var suite = Suite(expectedExit: 0, expectedStdout: "OK\n");
        var result = Run(Python, PythonTask("print('MISMATCH')", suite: suite));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Fail));
    }

    [Test]
    public void ResourceVerdictIsReportedAsIsTest()
    {
        var suite = Suite(expectedExit: 0);
        var limits = new VerifyLimitsData { FuelBudget = 50_000_000, WallTimeMs = 30_000 };
        var result = Run(Python, PythonTask("while True: pass", suite: suite, limits: limits));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
    }

    private static VerifySuiteData Suite(int expectedExit, string? expectedStdout = null)
    {
        return new VerifySuiteData
        {
            Cases = [new VerifySuiteCaseData { ExpectedExitCode = expectedExit, ExpectedStdout = expectedStdout }]
        };
    }
}
