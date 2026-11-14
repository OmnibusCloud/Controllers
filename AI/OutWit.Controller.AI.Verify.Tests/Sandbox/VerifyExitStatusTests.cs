using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// A program's exit status, whatever it is, is the program's: on every runtime an exit of 126 or
/// more (which WASI preview 1 does not have, and which wasmtime's own <c>proc_exit</c> refuses as
/// an engine error) is a nonzero exit like any other — <c>RuntimeError</c> with its code and its
/// output — and no longer fails the batch it is in. Below 126 nothing changed.
/// </summary>
[TestFixture]
public sealed class VerifyExitStatusTests : VerifySandboxTestBase
{
    #region Constants

    private const int STATUS = 200;

    #endregion

    #region Tests

    [TestCase(VerifyRuntimeCatalog.PYTHON_3_14_6, "m.py", "import sys\nprint('bye')\nsys.exit(200)")]
    [TestCase(VerifyRuntimeCatalog.PYTHON_3_14_6_NUMPY_2_5_4, "m.py", "import sys\nprint('bye')\nsys.exit(200)")]
    [TestCase(VerifyRuntimeCatalog.RUBY_3_4_1, "m.rb", "puts 'bye'\nexit 200")]
    [TestCase(VerifyRuntimeCatalog.LUA_5_4_8, "m.lua", "print('bye')\nos.exit(200)")]
    [TestCase(VerifyRuntimeCatalog.QUICKJS_0_15_1, "m.js", "import * as std from 'qjs:std';\nconsole.log('bye');\nstd.exit(200);")]
    [TestCase(VerifyRuntimeCatalog.TYPESCRIPT_SUCRASE_3_35_1, "m.ts", "import * as std from 'qjs:std';\nconst status: number = 200;\nconsole.log('bye');\nstd.exit(status);")]
    [TestCase(VerifyRuntimeCatalog.CLANG_22_1_0, "m.c", "#include <stdio.h>\nint main(void) { puts(\"bye\"); return 200; }\n")]
    [TestCase(VerifyRuntimeCatalog.CLANG_22_1_0, "m.cpp", "#include <cstdio>\n#include <cstdlib>\nint main() { std::puts(\"bye\"); std::exit(200); }\n")]
    public void AnExitStatusAbove125IsANonzeroExitTest(string runtimeId, string entry, string source)
    {
        var result = Run(Runtime(runtimeId), Task(runtimeId, entry, source));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError), result.Stderr);
            Assert.That(result.ExitCode, Is.EqualTo(STATUS));
            Assert.That(result.Stdout, Is.EqualTo("bye\n"), "what the program printed before it exited");
            Assert.That(result.Stderr, Is.Empty, "an exit has no words of the host's");
        });
    }

    [TestCase(STATUS, STATUS)]
    [TestCase(126, 126)]
    [TestCase(255, 255)]
    [TestCase(-1, -1)]
    public void ARawModuleExitsWithAnyStatusTest(int status, int expected)
    {
        var result = Run(Runtime(VerifyRuntimeCatalog.WASI), Task(VerifyRuntimeCatalog.WASI, "exit.wasm", Convert.ToBase64String(WasiModules.Exit(status))));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(expected), "WASI's unsigned status, read as a signed number");
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    [TestCase(VerifyRuntimeCatalog.PYTHON_3_14_6, "m.py", "import sys\nsys.exit(125)", 125)]
    [TestCase(VerifyRuntimeCatalog.PYTHON_3_14_6, "m.py", "import sys\nsys.exit(3)", 3)]
    [TestCase(VerifyRuntimeCatalog.LUA_5_4_8, "m.lua", "os.exit(3)", 3)]
    [TestCase(VerifyRuntimeCatalog.CLANG_22_1_0, "m.c", "int main(void) { return 1; }\n", 1)]
    public void AnExitStatusBelow126IsAsItWasTest(string runtimeId, string entry, string source, int status)
    {
        var result = Run(Runtime(runtimeId), Task(runtimeId, entry, source));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(status));
        });
    }

    [Test]
    public void ASuiteCaseMayExpectAnExitStatusAbove125Test()
    {
        var suite = new VerifySuiteData
        {
            Cases =
            [
                new VerifySuiteCaseData { Stdin = "200", ExpectedExitCode = 200 },
                new VerifySuiteCaseData { Stdin = "3", ExpectedExitCode = 3 },
                new VerifySuiteCaseData { Stdin = "0" }
            ]
        };

        var result = Run(Python, PythonTask("import sys\nsys.exit(int(sys.stdin.read()))", suite: suite));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.CaseResults.Select(c => c.ExitCode), Is.EqualTo(new[] { 200, 3, 0 }));
        });
    }

    [Test]
    public void AnExitStatusAbove125NoLongerFailsItsBatchTest()
    {
        // Before 0.12.0 the engine's error left the batch: one such task took its batch's other
        // results with it. Now each task of the batch has its verdict.
        var batch = new VerifyTaskBatchData
        {
            RuntimeId = VerifyRuntimeCatalog.PYTHON_3_14_6,
            Tasks =
            [
                PythonTask("print(1)", index: 0),
                PythonTask("import sys\nsys.exit(200)", index: 1),
                PythonTask("print(2)", index: 2)
            ]
        };

        var results = VerifyBatchExecutor.Execute(Sandbox, RuntimesRoot, batch).Results.OrderBy(r => r.TaskIndex);

        Assert.That(results.Select(r => (r.Verdict, r.ExitCode)), Is.EqualTo(new[]
        {
            (VerifyVerdict.Pass, 0), (VerifyVerdict.RuntimeError, STATUS), (VerifyVerdict.Pass, 0)
        }));
    }

    #endregion

    #region Tools

    private VerifyResolvedRuntime Runtime(string runtimeId)
    {
        var runtime = VerifyRuntimeCatalog.Resolve(RuntimesRoot, runtimeId, out var reason);
        Assert.That(runtime, Is.Not.Null, reason);
        return runtime!;
    }

    private static VerifyTaskData Task(string runtimeId, string entry, string source)
    {
        return new VerifyTaskData
        {
            RuntimeId = runtimeId,
            EntryPoint = entry,
            Sources = [new VerifySourceFileData { Name = entry, Content = source }]
        };
    }

    #endregion
}
