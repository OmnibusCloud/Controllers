using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// What a release must hold beyond verdicts: the sandbox leaves nothing behind, the guest
/// sees only the pinned world, inputs of every shape arrive whole, caps are flagged, a suite's
/// arguments compose, and a cancelled job stops between cases.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifySandboxHygieneTests : VerifySandboxTestBase
{
    #region Hygiene Tests

    [Test]
    public void StdioFilesAreRemovedAfterEveryRunTest()
    {
        var suite = new VerifySuiteData { Cases = [new VerifySuiteCaseData { Stdin = "1\n", ExpectedStdout = "1\n" }] };
        for (var i = 0; i < 12; i++)
        {
            Run(Python, PythonTask("print(input())", suite: suite));
            Run(QuickJs, JsTask("console.log(1)"));
            Run(Python, PythonTask("raise SystemExit(3)"));
        }

        Assert.That(Directory.GetFiles(Sandbox.IoDirectory), Is.Empty, "every run deletes its stdin/stdout/stderr files");
    }

    [Test]
    public void DisposeRemovesTheIoFolderTest()
    {
        var sandbox = new VerifyWasmSandbox();
        var folder = sandbox.IoDirectory;
        sandbox.Execute(Python, PythonTask("print(1)"), VerifySandboxDefaults.Resolve(null, null));
        Assert.That(Directory.Exists(folder), Is.True);

        sandbox.Dispose();

        Assert.That(Directory.Exists(folder), Is.False);
    }

    [Test]
    public void GuestSeesOnlyThePinnedEnvironmentTest()
    {
        var result = Run(Python, PythonTask("import os; print(sorted(os.environ))"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass));
            Assert.That(result.Stdout, Is.EqualTo("['PYTHONDONTWRITEBYTECODE', 'PYTHONHASHSEED', 'PYTHONHOME', 'PYTHONUNBUFFERED']\n"),
                "no host variable leaks into the guest");
        });

        var withLayer = Run(PythonSympy, SympyTask("import os; print(os.environ['PYTHONPATH'])"));
        Assert.That(withLayer.Stdout, Is.EqualTo("/lib/sympy-1.14.0.zip\n"));
    }

    [Test]
    public void LargeStdinArrivesWholeTest()
    {
        var stdin = new string('x', 1 << 20) + "\n";
        var result = Run(Python, PythonTask("import sys; d = sys.stdin.read(); print(len(d), d[-2])", stdin: stdin));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
        Assert.That(result.Stdout, Is.EqualTo($"{(1 << 20) + 1} x\n"));
    }

    [Test]
    public void UnicodeArgumentsAndOutputRoundTripTest()
    {
        var args = new List<string> { "привет", "日本語", "é", "a b" };
        var result = Run(Python, PythonTask("import sys; print('|'.join(sys.argv[1:]))", args: args));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
        Assert.That(result.Stdout, Is.EqualTo("привет|日本語|é|a b\n"));
    }

    [Test]
    public void StderrIsCappedAndFlaggedWithoutChangingTheVerdictTest()
    {
        var result = Run(Python, PythonTask("import sys\nsys.stderr.write('e' * 10000)\nprint('ok')"),
            new VerifyLimitsData { StderrLimitBytes = 1024 });

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass));
            Assert.That(result.Stdout, Is.EqualTo("ok\n"));
            Assert.That(result.StderrTruncated, Is.True);
            Assert.That(result.Stderr, Has.Length.EqualTo(1024));
        });
    }

    [Test]
    public void SuiteCaseArgumentsAppendToTheTasksTest()
    {
        var suite = new VerifySuiteData
        {
            Cases =
            [
                new VerifySuiteCaseData { Args = ["b"], ExpectedStdout = "a b\n" },
                new VerifySuiteCaseData { Args = ["c", "d"], ExpectedStdout = "a c d\n" }
            ]
        };
        var result = Run(Python, PythonTask("import sys; print(' '.join(sys.argv[1:]))", args: ["a"], suite: suite));

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
        Assert.That(result.CaseResults.Select(c => c.ActualStdout), Is.EqualTo(new[] { "a b\n", "a c d\n" }));
    }

    [Test]
    public void SuiteStopsBetweenCasesWhenCancelledTest()
    {
        var suite = new VerifySuiteData
        {
            Cases = [new VerifySuiteCaseData { ExpectedStdout = "1\n" }, new VerifySuiteCaseData { ExpectedStdout = "1\n" }]
        };
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            Sandbox.Execute(Python, PythonTask("print(1)", suite: suite), VerifySandboxDefaults.Resolve(null, null), cancelled.Token));
    }

    [Test]
    public void BatchStopsAtTheNextTaskBoundaryWhenCancelledTest()
    {
        // Twelve tasks, degree 1, cancel after the second completes: the pool starts no more.
        var tasks = Enumerable.Range(0, 12).Select(i => PythonTask("print(1)", i)).ToList();
        var batch = new VerifyTaskBatchData { RuntimeId = VerifyRuntimeCatalog.PYTHON_3_14_6, Tasks = tasks, MaxDegree = 1 };
        using var cts = new CancellationTokenSource();
        var completed = 0;

        var exception = Assert.Throws<OperationCanceledException>(() =>
            VerifyBatchExecutor.Execute(Sandbox, RuntimesRoot, batch, Guid.Empty, (done, _) =>
            {
                completed = done;
                if (done == 2)
                    cts.Cancel();
            }, cts.Token));

        Assert.That(exception, Is.Not.Null);
        Assert.That(completed, Is.InRange(2, 3), "at most the task already running finishes after the cancel");
    }

    #endregion

    #region Helpers

    private static VerifyTaskData SympyTask(string code)
    {
        var task = PythonTask(code);
        task.RuntimeId = VerifyRuntimeCatalog.PYTHON_3_14_6_SYMPY_1_14_0;
        return task;
    }

    #endregion
}
