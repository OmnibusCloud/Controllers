using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The fuel of a hello on every runtime, pinned. Since 0.12.0 the budget is given after the module
/// is instantiated, so the fuel a task reports is its program's own: the copy of the runtime's data
/// segments, which wasmtime charged a unit a byte for where it copies them and not where Linux maps
/// a copy-on-write image, is out of it, and with it the fixed step earlier versions showed between
/// Linux and the other systems (a Lua hello was 32,769 more on Windows and macOS). The build server
/// runs these on Windows, Linux and macOS against the same pins: one number for every runtime but
/// the two CPython ones that read their library from files, which still drift between hosts by up to
/// a few hundredths of a percent with how each host's WASI answers the interpreter's file queries
/// (measured: equal on this desktop and on macOS, 40 units less on the Windows build server, 1,113
/// and 969,160 less on Linux); the numpy runtime starts from a snapshot and does not.
/// </summary>
[TestFixture]
public sealed class VerifyHelloFuelTests : VerifySandboxTestBase
{
    #region Constants

    /// <summary>The most a CPython task's fuel drifts between hosts: 0.06 % (documented since 0.6; measured up to 0.032 %).</summary>
    private const double CPYTHON_DRIFT = 0.0006;

    #endregion

    #region Tests

    [TestCase(VerifyRuntimeCatalog.PYTHON_3_14_6_NUMPY_2_5_4, "m.py", "import numpy; print(numpy.__version__)", 24_917_573L)]
    [TestCase(VerifyRuntimeCatalog.QUICKJS_0_15_1, "m.js", "console.log('hello')", 2_359_031L)]
    [TestCase(VerifyRuntimeCatalog.RUBY_3_4_1, "m.rb", "puts 'hello'", 153_866_679L)]
    [TestCase(VerifyRuntimeCatalog.LUA_5_4_8, "m.lua", "print('hello')", 571_410L)]
    [TestCase(VerifyRuntimeCatalog.TYPESCRIPT_SUCRASE_3_35_1, "m.ts", "const x: string = 'hello';\nconsole.log(x);", 333_147_484L)]
    [TestCase(VerifyRuntimeCatalog.CLANG_22_1_0, "m.c", "#include <stdio.h>\nint main(void) { puts(\"hello\"); return 0; }\n", 710L)]
    [TestCase(VerifyRuntimeCatalog.WASI, "hello.wasm", global::OutWit.Controller.AI.Verify.Sandbox.VerifySelfCheck.HELLO_WASM_BASE64, 6L)]
    public void AHelloCostsOnePinnedFuelEverywhereTest(string runtimeId, string entry, string source, long fuel)
    {
        var result = Hello(runtimeId, entry, source);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.FuelConsumed, Is.EqualTo(fuel));
        });
    }

    [TestCase(VerifyRuntimeCatalog.PYTHON_3_14_6, "m.py", "print('hello')", 121_262_306L, 3_170_305L)]
    [TestCase(VerifyRuntimeCatalog.PYTHON_3_14_6_SYMPY_1_14_0, "m.py", "import sympy; print(sympy.__version__)", 3_058_460_132L, 3_170_305L)]
    public void ACPythonHelloCostsItsPinnedFuelWithinTheFileDriftTest(string runtimeId, string entry, string source, long fuel, long instantiation)
    {
        var result = Hello(runtimeId, entry, source);
        var drift = (long)(fuel * CPYTHON_DRIFT);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.FuelConsumed, Is.EqualTo(fuel).Within(drift), "the pin (Windows and macOS), within the drift between hosts");
            Assert.That(drift, Is.LessThan(instantiation), "the drift allowed is smaller than the instantiation term the fuel no longer carries");
        });
    }

    #endregion

    #region Tools

    private VerifyResultData Hello(string runtimeId, string entry, string source)
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
        return result;
    }

    #endregion
}
