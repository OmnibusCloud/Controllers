using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Solver;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Smt.Sandbox;

/// <summary>
/// Proves that a deployed smt.module is self-contained: the managed wasmtime binding and its
/// native library resolve from the module's folder, the solver module is found next to the
/// controller, the sandbox compiles it, and one problem is solved to its answer. The module-load
/// test calls it by reflection across the plugin's isolated load context, and a node's operator can
/// call it as a doctor-style check; it returns plain text, so that no type has to cross a
/// load-context boundary. The report is the machine's own and goes nowhere else: it names the
/// folders it found things in.
/// </summary>
internal static class SmtSelfCheck
{
    #region Constants

    /// <summary>Line: the sandbox was made and the native library loaded.</summary>
    public const string NATIVE_LOADED = "wasmtime-native: loaded";

    /// <summary>Line prefix: the sandbox could not be made.</summary>
    public const string SANDBOX_FAILED = "sandbox: FAILED";

    /// <summary>Line prefix: where the managed binding resolved from.</summary>
    public const string MANAGED_PREFIX = "wasmtime-managed: ";

    /// <summary>Line prefix: the native library the module-aware resolver would load.</summary>
    public const string NATIVE_PATH_PREFIX = "wasmtime-native-path: ";

    /// <summary>Line prefix: where the solver module was found.</summary>
    public const string MODULE_PREFIX = "solver-module: ";

    /// <summary>Line prefix: the probe problem's outcome.</summary>
    public const string PROBE_PREFIX = "probe: ";

    private const string PROBE = "(set-logic QF_LIA)(declare-const x Int)(declare-const y Int)(assert (= (+ x y) 10))(assert (= (- x y) 4))";

    #endregion

    #region Functions

    /// <summary>
    /// Runs the self-check and returns a line-per-fact report: the controller and binding
    /// locations, the platform, the native library the resolver would load, the solver module,
    /// whether the sandbox was made (with the compile time and the trap mode, or the reason it was
    /// not), and the probe problem's outcome.
    /// </summary>
    /// <returns>The report text; never throws for a missing or broken sandbox.</returns>
    public static string Run()
    {
        var report = new StringBuilder();
        report.AppendLine($"controller: {typeof(SmtSelfCheck).Assembly.Location}");
        report.AppendLine($"{MANAGED_PREFIX}{typeof(Wasmtime.Engine).Assembly.Location}");
        report.AppendLine($"rid: {RuntimeInformation.RuntimeIdentifier}");
        report.AppendLine($"{NATIVE_PATH_PREFIX}{WasmHostNativeLibrary.Locate() ?? "(none)"}");
        report.AppendLine($"{MODULE_PREFIX}{SmtModuleLocator.Locate() ?? "(none)"}");

        try
        {
            var sandbox = SmtSandboxHost.Get(out var problem);
            if (sandbox == null)
            {
                report.AppendLine($"{SANDBOX_FAILED} {problem}");
                return report.ToString();
            }

            report.AppendLine(NATIVE_LOADED);
            report.AppendLine($"sandbox: compiled in {sandbox.CompileMs.ToString(CultureInfo.InvariantCulture)} ms, traps {(sandbox.ExplicitTraps ? "explicit" : "signals")}");

            var taken = SmtSanitizer.Sanitize(PROBE).Problem!;
            var outcome = SmtSolver.Solve(new SmtProblemData { Id = "probe", Text = taken.Canonical, Hash = taken.Hash }, new SmtOptionsData(), sandbox, null);
            report.AppendLine($"{PROBE_PREFIX}{SmtBenchmark.Words(outcome)} fuel={outcome.Fuel.ToString(CultureInfo.InvariantCulture)}");
        }
        catch (Exception exception)
        {
            report.AppendLine($"{SANDBOX_FAILED} {exception.GetType().Name}: {exception.Message}");
        }

        return report.ToString();
    }

    #endregion
}
