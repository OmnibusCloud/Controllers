using System.Runtime.InteropServices;
using System.Text;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Proves that a deployed verify.module is self-contained: the managed wasmtime binding and
/// its native library resolve from the module folder, the language runtimes are found next
/// to the controller, and one task per known runtime runs to a verdict. The module-load test
/// calls it by reflection across the plugin's isolated load context, and a node operator can
/// call it as a doctor-style check; it returns plain text so that no Model type has to cross
/// a load-context boundary.
/// </summary>
public static class VerifySelfCheck
{
    #region Constants

    /// <summary>Self-check line: the native wasmtime loaded.</summary>
    public const string NATIVE_LOADED = "wasmtime-native: loaded";
    /// <summary>Self-check line prefix: the native wasmtime failed to load.</summary>
    public const string NATIVE_FAILED = "wasmtime-native: FAILED";
    /// <summary>Self-check line prefix: where the managed binding resolved from.</summary>
    public const string MANAGED_PREFIX = "wasmtime-managed: ";
    /// <summary>Self-check line prefix: the language-runtime root.</summary>
    public const string WASM_ROOT_PREFIX = "wasm-root: ";
    /// <summary>Self-check line prefix: the native library the module-aware resolver would load.</summary>
    public const string NATIVE_PATH_PREFIX = "wasmtime-native-path: ";
    /// <summary>Self-check line prefix: the native library this module's resolver loaded, the one another controller's copy of the host loaded, or that the load context resolved it.</summary>
    public const string NATIVE_RESOLVER_PREFIX = "wasmtime-native-resolver: ";

    /// <summary>
    /// A 224-byte WASI command module that writes "hello\n" (fd_write) and returns — the raw
    /// wasi runtime's probe, and the example the README shows. Assembled by hand; the tests
    /// pin it against their own assembler.
    /// </summary>
    public const string HELLO_WASM_BASE64 =
        "AGFzbQEAAAABEANgBH9/f38Bf2ABfwBgAAACZwMWd2FzaV9zbmFwc2hvdF9wcmV2aWV3MQhmZF93cml0ZQAAFndhc2lfc25hcHNob3RfcHJldmlldzEHZmRfcmVhZAAAFndhc2lfc25hcHNob3RfcHJldmlldzEJcHJvY19leGl0AAEDAgECBQMBAAEHEwIGbWVtb3J5AgAGX3N0YXJ0AAMKDwENAEEBQQBBAUEIEAAaCwssAQBBAAsmIAAAAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABoZWxsbwo=";

    #endregion

    #region Fields

    private static readonly (string Id, string Entry, string Source)[] PROBES =
    [
        (VerifyRuntimeCatalog.PYTHON_3_14_6, "m.py", "print(6*7)"),
        (VerifyRuntimeCatalog.PYTHON_3_14_6_SYMPY_1_14_0, "m.py", "import sympy; print(sympy.__version__)"),
        (VerifyRuntimeCatalog.PYTHON_3_14_6_NUMPY_2_5_4, "m.py", "import numpy; print(numpy.__version__, int(numpy.arange(7).sum()) * 2)"),
        (VerifyRuntimeCatalog.QUICKJS_0_15_1, "m.js", "console.log(6*7)"),
        (VerifyRuntimeCatalog.RUBY_3_4_1, "m.rb", "require 'prime'; puts 6*7 if 7.prime?"),
        (VerifyRuntimeCatalog.LUA_5_4_8, "m.lua", "print(select(2, pcall(error, 6*7)))"),
        (VerifyRuntimeCatalog.TYPESCRIPT_SUCRASE_3_35_1, "m.ts", "const x: number = 6 * 7;\nconsole.log(x);"),
        (VerifyRuntimeCatalog.CLANG_22_1_0, "m.cpp", "#include <cstdio>\nint main() { std::printf(\"%d\\n\", 6 * 7); }\n"),
        (VerifyRuntimeCatalog.WASI, "hello.wasm", HELLO_WASM_BASE64)
    ];

    #endregion

    #region Functions

    /// <summary>
    /// Runs the self-check and returns a line-per-fact report: the controller and binding
    /// locations, the RID, the native library the module-aware resolver would load, the
    /// language-runtime root, whether the native library loaded (and through which path),
    /// and one verdict line per known runtime. A sandbox that cannot start is the
    /// <see cref="NATIVE_FAILED"/> line with the reason: a wasmtime binding other than the one
    /// this controller was built against names both versions there (the host's binding guard), a
    /// native library other than the pinned one names both hashes (its natives guard).
    /// </summary>
    /// <returns>The report text; never throws for a missing or broken sandbox.</returns>
    public static string Run()
    {
        var report = new StringBuilder();
        report.AppendLine($"controller: {typeof(VerifySelfCheck).Assembly.Location}");
        report.AppendLine($"{MANAGED_PREFIX}{typeof(Wasmtime.Engine).Assembly.Location}");
        report.AppendLine($"rid: {RuntimeInformation.RuntimeIdentifier}");
        report.AppendLine($"{NATIVE_PATH_PREFIX}{VerifyNativeLibrary.Locate() ?? "(none)"}");

        var root = VerifyRuntimeLocator.Locate();
        report.AppendLine($"{WASM_ROOT_PREFIX}{root ?? "(none)"}");

        try
        {
            using var sandbox = new VerifyWasmSandbox();
            report.AppendLine(NATIVE_LOADED);
            report.AppendLine($"{NATIVE_RESOLVER_PREFIX}{ResolverLine()}");

            foreach (var (id, entry, source) in PROBES)
                report.AppendLine(Probe(sandbox, root, id, entry, source));
        }
        catch (Exception ex)
        {
            report.AppendLine($"{NATIVE_FAILED} {ex.GetType().Name}: {ex.Message}");
        }

        return report.ToString();
    }

    private static string ResolverLine()
    {
        if (VerifyNativeLibrary.ResolvedPath is { } own)
            return own;

        return VerifyNativeLibrary.LoadedByAnotherController
            ? $"{VerifyNativeLibrary.LoadedPath} (loaded by another controller's copy of the wasm host)"
            : "not used (the load context resolved the library)";
    }

    private static string Probe(VerifyWasmSandbox sandbox, string? root, string id, string entry, string source)
    {
        // A task-supplied runtime needs no root; the others report its absence as such.
        var runtime = VerifyRuntimeCatalog.Resolve(root, id, out var reason);
        if (runtime == null)
            return root == null && VerifyRuntimeCatalog.Find(id)?.TaskSupplied != true
                ? $"{id}: no wasm root"
                : $"{id}: unavailable ({reason})";

        var task = new VerifyTaskData
        {
            TaskIndex = 0,
            RuntimeId = id,
            EntryPoint = entry,
            Sources = [new VerifySourceFileData { Name = entry, Content = source }]
        };

        var result = sandbox.Execute(runtime, task, VerifySandboxDefaults.Resolve(null, null));
        return $"{id}: {result.Verdict} exit={result.ExitCode} stdout={result.Stdout.Trim()}";
    }

    #endregion
}
