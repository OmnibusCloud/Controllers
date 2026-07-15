using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// One behavioral test per verdict path of the ExecuteBatch primitive: each hostile or
/// benign program lands in the correct verdict without harming the host, and the
/// deterministic-imports shadowing makes output — and fuel — reproducible.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyExecuteVerdictTests : VerifySandboxTestBase
{
    #region Verdict Tests

    [Test]
    public void CleanRunWithoutSuiteIsPassTest()
    {
        var result = Run(Python, PythonTask("print('ok')"));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass));
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.Stdout.Trim(), Is.EqualTo("ok"));
    }

    [Test]
    public void SuiteMatchIsPassTest()
    {
        var suite = new VerifySuiteData
        {
            Cases =
            [
                new VerifySuiteCaseData { Stdin = "2\n3\n", ExpectedStdout = "5\n", ExpectedExitCode = 0 },
                new VerifySuiteCaseData { Stdin = "10\n20\n", ExpectedStdout = "30\n", ExpectedExitCode = 0 }
            ]
        };
        var result = Run(Python, PythonTask("a=int(input()); b=int(input()); print(a+b)", suite: suite));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass));
        Assert.That(result.CaseResults, Has.Count.EqualTo(2));
        Assert.That(result.CaseResults.All(c => c.Passed), Is.True);
    }

    [Test]
    public void SuiteMismatchIsFailTest()
    {
        var suite = new VerifySuiteData
        {
            Cases =
            [
                new VerifySuiteCaseData { Stdin = "2\n3\n", ExpectedStdout = "5\n", ExpectedExitCode = 0 },
                new VerifySuiteCaseData { Stdin = "2\n3\n", ExpectedStdout = "6\n", ExpectedExitCode = 0 }  // wrong
            ]
        };
        var result = Run(Python, PythonTask("a=int(input()); b=int(input()); print(a+b)", suite: suite));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Fail));
        Assert.That(result.CaseResults[0].Passed, Is.True);
        Assert.That(result.CaseResults[1].Passed, Is.False);
    }

    [Test]
    public void UncaughtExceptionIsRuntimeErrorTest()
    {
        var result = Run(Python, PythonTask("raise ValueError('boom')"));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Stderr, Does.Contain("ValueError"));
    }

    [Test]
    public void FuelExhaustionIsTimeoutTest()
    {
        var result = Run(Python, PythonTask("while True: pass"),
            new VerifyLimitsData { FuelBudget = 2_000_000_000, WallTimeMs = 30_000 });

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
    }

    [Test]
    public void WallClockExhaustionIsTimeoutTest()
    {
        // Huge fuel so the epoch deadline is what fires, not fuel.
        var result = Run(Python, PythonTask("while True: pass"),
            new VerifyLimitsData { FuelBudget = 500_000_000_000, WallTimeMs = 500 });

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
    }

    [Test]
    public void MemoryBombIsMemoryExceededTest()
    {
        var result = Run(Python, PythonTask("b = bytearray(512*1024*1024); print(len(b))"),
            new VerifyLimitsData { MemoryBytes = 256L * 1024 * 1024, FuelBudget = 500_000_000_000, WallTimeMs = 30_000 });

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.MemoryExceeded));
    }

    [Test]
    public void StdoutFloodIsOutputExceededTest()
    {
        var result = Run(Python, PythonTask("print('x' * 5_000_000)"),
            new VerifyLimitsData { StdoutLimitBytes = 64 * 1024, FuelBudget = 500_000_000_000, WallTimeMs = 30_000 });

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.OutputExceeded));
        Assert.That(result.StdoutTruncated, Is.True);
        Assert.That(result.Stdout.Length, Is.LessThanOrEqualTo(64 * 1024));
    }

    [Test]
    public void UnknownRuntimeIsRuntimeUnavailableTest()
    {
        var task = PythonTask("print('x')");
        task.RuntimeId = "python-9.9.9";
        var batch = new VerifyTaskBatchData { RuntimeId = "python-9.9.9", Tasks = [task] };
        var result = OutWit.Controller.AI.Verify.Sandbox.VerifyBatchExecutor.Execute(Sandbox, RuntimesRoot, batch);

        Assert.That(result.Results[0].Verdict, Is.EqualTo(VerifyVerdict.RuntimeUnavailable));
    }

    [Test]
    public void JavaScriptRunsAndPassesTest()
    {
        var result = Run(QuickJs, JsTask("console.log(6 * 7)"));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass));
        Assert.That(result.Stdout.Trim(), Is.EqualTo("42"));
    }

    #endregion

    #region Determinism Tests

    [Test]
    public void OutputIsReproducibleAcrossRunsTest()
    {
        const string program = """
            import math, random
            random.seed(7)
            print(sum(math.sin(i) for i in range(5000)), [random.random() for _ in range(3)])
            """;

        var first = Run(Python, PythonTask(program));
        var second = Run(Python, PythonTask(program));

        Assert.That(first.Verdict, Is.EqualTo(VerifyVerdict.Pass));
        Assert.That(second.Stdout, Is.EqualTo(first.Stdout));
    }

    [Test]
    public void PinnedClockMakesFuelByteExactTest()
    {
        // The runtime spike found fuel drifting by ~50 instructions because CPython startup
        // read the WASI clock. With clock_time_get pinned to a constant, identical runs must
        // now burn the EXACT same fuel — the property that lets fuel double as a portable budget.
        var task = PythonTask("print(sum(i * i for i in range(100000)))");

        var first = Run(Python, task, new VerifyLimitsData { FuelBudget = 50_000_000_000 });
        var second = Run(Python, task, new VerifyLimitsData { FuelBudget = 50_000_000_000 });

        Assert.That(first.Verdict, Is.EqualTo(VerifyVerdict.Pass));
        Assert.That(first.FuelConsumed, Is.GreaterThan(0));
        Assert.That(second.FuelConsumed, Is.EqualTo(first.FuelConsumed));
    }

    [Test]
    public void SeededRandomnessIsDeterministicTest()
    {
        // os.urandom draws from the sandbox's seeded random_get — identical across runs of
        // the same task, and different when the task seed differs.
        var taskA = PythonTask("import os; print(os.urandom(16).hex())");
        taskA.RandomSeed = 111;
        var taskB = PythonTask("import os; print(os.urandom(16).hex())");
        taskB.RandomSeed = 222;

        var a1 = Run(Python, taskA);
        var a2 = Run(Python, taskA);
        var b1 = Run(Python, taskB);

        Assert.That(a1.Stdout, Is.EqualTo(a2.Stdout), "same seed must reproduce");
        Assert.That(b1.Stdout, Is.Not.EqualTo(a1.Stdout), "different seed must differ");
    }

    #endregion
}
