using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// QuickJS as the toolchain builds it: the C stack below the data and the engine's stack limit
/// on. The release's own WASI build placed a 64 KiB stack after its data with the limit off, so
/// a deep recursion wrote over the interpreter's data: a correct program ended with exit 0 and no
/// output, an endless one got a verdict that depended on what ran before it. Here a recursion
/// too deep is JavaScript's RangeError, before the engine's native stack runs out, on every
/// platform the build server runs.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyQuickJsRuntimeTests : VerifySandboxTestBase
{
    #region Constants

    private const string RANGE_ERROR = "RangeError: Maximum call stack size exceeded";

    #endregion

    #region Recursion Tests

    [Test]
    public void TheRecursionTheReleaseBuildEndedSilentlyPrintsItsResultTest()
    {
        const string program =
            "function f(n){ const a = [n, n + 1, n + 2]; return n === 0 ? 0 : a[0] + f(n - 1); }\n" +
            "console.log(f(200));\n" +
            "console.log('after');\n";

        var result = Run(QuickJs, JsTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("20100\nafter\n"));
        });
    }

    // Plain recursion, a frame with locals, through a native callback, through a conversion.
    [TestCase("function f(n){ return 1 + f(n + 1); }")]
    [TestCase("function f(n){ const a = [n, n + 1, n + 2]; return a[0] + f(n + 1); }")]
    [TestCase("function f(n){ return [n].map(x => f(x + 1))[0]; }")]
    [TestCase("function f(n){ return String({ toString(){ return 'x' + f(n + 1); } }); }")]
    public void AnEndlessRecursionIsARangeErrorTheProgramCatchesTest(string function)
    {
        var program = function + "\ntry { f(0); console.log('returned'); } catch (e) { console.log('caught ' + e.name); }\nconsole.log('after');\n";

        var result = Run(QuickJs, JsTask(program));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("caught RangeError\nafter\n"));
        });
    }

    [Test]
    public void AnUncaughtEndlessRecursionIsARuntimeErrorTest()
    {
        var result = Run(QuickJs, JsTask("function f(n){ return 1 + f(n + 1); }\nconsole.log('before');\nf(0);\n"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Stdout, Is.EqualTo("before\n"));
            Assert.That(result.Stderr, Does.StartWith(RANGE_ERROR));
        });
    }

    [Test]
    public void AnEndlessRecursionEndsTheSameAfterOtherTasksTest()
    {
        // The release build's verdict for this program changed with what the sandbox had run
        // before it; now it is the same alone and after a mix of other programs.
        var endless = JsTask("const g = (a, b) => g(b, a % b);\nconsole.log(g(12, 18));\n");
        var alone = Run(QuickJs, endless);

        using (var sandbox = new VerifyWasmSandbox())
        {
            var limits = VerifySandboxDefaults.Resolve(null, null);
            foreach (var other in new[] { "console.log(6 * 7)", "throw new Error('boom')", "let s = 0; for (let i = 0; i < 1e6; i++) s += i; console.log(s)" })
                sandbox.Execute(QuickJs, JsTask(other), limits);
            sandbox.Execute(Python, PythonTask("print(sum(range(10)))"), limits);

            var after = sandbox.Execute(QuickJs, endless, limits);

            Assert.Multiple(() =>
            {
                Assert.That(alone.Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
                Assert.That(alone.Stderr, Does.StartWith(RANGE_ERROR));
                Assert.That(after.Verdict, Is.EqualTo(alone.Verdict));
                Assert.That(after.Stderr, Is.EqualTo(alone.Stderr));
                Assert.That(after.FuelConsumed, Is.EqualTo(alone.FuelConsumed));
            });
        }
    }

    #endregion
}
