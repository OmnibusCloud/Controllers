using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The fuel of a hello on every runtime, pinned. Since 0.12.0 the budget is given after the module
/// is instantiated, so the fuel a task reports is its program's own: the copy of the runtime's data
/// segments, which Windows (and macOS for some modules) paid a unit a byte for and Linux, with a
/// copy-on-write image, did not, is out of it — the "fixed step" between Linux and the other
/// systems (a Lua hello was 32,769 more on Windows and macOS) is gone. The build server runs these
/// on Windows, Linux and macOS against the same pins.
/// </summary>
[TestFixture]
public sealed class VerifyHelloFuelTests : VerifySandboxTestBase
{
    #region Tests

    [TestCase(VerifyRuntimeCatalog.PYTHON_3_14_6, "m.py", "print('hello')", 121_262_306L)]
    [TestCase(VerifyRuntimeCatalog.PYTHON_3_14_6_SYMPY_1_14_0, "m.py", "import sympy; print(sympy.__version__)", 3_058_460_132L)]
    [TestCase(VerifyRuntimeCatalog.PYTHON_3_14_6_NUMPY_2_5_4, "m.py", "import numpy; print(numpy.__version__)", 24_917_573L)]
    [TestCase(VerifyRuntimeCatalog.QUICKJS_0_15_1, "m.js", "console.log('hello')", 2_359_031L)]
    [TestCase(VerifyRuntimeCatalog.RUBY_3_4_1, "m.rb", "puts 'hello'", 153_866_679L)]
    [TestCase(VerifyRuntimeCatalog.LUA_5_4_8, "m.lua", "print('hello')", 571_410L)]
    [TestCase(VerifyRuntimeCatalog.TYPESCRIPT_SUCRASE_3_35_1, "m.ts", "const x: string = 'hello';\nconsole.log(x);", 333_147_484L)]
    [TestCase(VerifyRuntimeCatalog.CLANG_22_1_0, "m.c", "#include <stdio.h>\nint main(void) { puts(\"hello\"); return 0; }\n", 710L)]
    [TestCase(VerifyRuntimeCatalog.WASI, "hello.wasm", global::OutWit.Controller.AI.Verify.Sandbox.VerifySelfCheck.HELLO_WASM_BASE64, 6L)]
    public void AHelloCostsItsPinnedFuelTest(string runtimeId, string entry, string source, long fuel)
    {
        var runtime = VerifyRuntimeCatalog.Resolve(RuntimesRoot, runtimeId, out var reason);
        Assert.That(runtime, Is.Not.Null, reason);

        var result = Run(runtime!, new VerifyTaskData
        {
            RuntimeId = runtimeId,
            EntryPoint = entry,
            Sources = [new VerifySourceFileData { Name = entry, Content = source }]
        });
        TestContext.Out.WriteLine($"{runtimeId}: {result.Verdict} fuel={result.FuelConsumed} on {VerifyRuntimeDiagnosticsBuilder.RuntimeTarget()}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.FuelConsumed, Is.EqualTo(fuel));
        });
    }

    #endregion
}
