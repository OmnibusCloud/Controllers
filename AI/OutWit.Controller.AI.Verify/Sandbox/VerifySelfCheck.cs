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

    public const string NATIVE_LOADED = "wasmtime-native: loaded";
    public const string NATIVE_FAILED = "wasmtime-native: FAILED";
    public const string MANAGED_PREFIX = "wasmtime-managed: ";
    public const string WASM_ROOT_PREFIX = "wasm-root: ";

    #endregion

    #region Fields

    private static readonly (string Id, string Entry, string Source)[] PROBES =
    [
        (VerifyRuntimeCatalog.PYTHON_3_14_6, "m.py", "print(6*7)"),
        (VerifyRuntimeCatalog.QUICKJS_0_15_1, "m.js", "console.log(6*7)")
    ];

    #endregion

    #region Functions

    /// <summary>
    /// Runs the self-check and returns a line-per-fact report: the controller and binding
    /// locations, the RID, the language-runtime root, whether the native library loaded,
    /// and one verdict line per known runtime.
    /// </summary>
    /// <returns>The report text; never throws for a missing or broken sandbox.</returns>
    public static string Run()
    {
        var report = new StringBuilder();
        report.AppendLine($"controller: {typeof(VerifySelfCheck).Assembly.Location}");
        report.AppendLine($"{MANAGED_PREFIX}{typeof(Wasmtime.Engine).Assembly.Location}");
        report.AppendLine($"rid: {RuntimeInformation.RuntimeIdentifier}");

        var root = VerifyRuntimeLocator.Locate();
        report.AppendLine($"{WASM_ROOT_PREFIX}{root ?? "(none)"}");

        try
        {
            using var sandbox = new VerifyWasmSandbox();
            report.AppendLine(NATIVE_LOADED);

            foreach (var (id, entry, source) in PROBES)
                report.AppendLine(Probe(sandbox, root, id, entry, source));
        }
        catch (Exception ex)
        {
            report.AppendLine($"{NATIVE_FAILED} {ex.GetType().Name}: {ex.Message}");
        }

        return report.ToString();
    }

    private static string Probe(VerifyWasmSandbox sandbox, string? root, string id, string entry, string source)
    {
        if (root == null)
            return $"{id}: no wasm root";

        var runtime = VerifyRuntimeCatalog.Resolve(root, id, out var reason);
        if (runtime == null)
            return $"{id}: unavailable ({reason})";

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
