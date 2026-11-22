using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// Builds the sandbox runtime inventory of the process it runs in: the wasmtime host version
/// and, for each known runtime, whether its module is present and hash-verified. A plain
/// script function evaluates on the host, so through <c>VerifyRuntimeDiagnostics</c> this
/// describes the server's module; a node's module is exercised by <c>Verify.ExecutePackage</c>.
/// </summary>
public static class VerifyRuntimeDiagnosticsBuilder
{
    /// <summary>Builds the inventory of this process: wasmtime version, runtime target, every known runtime with its availability and pin.</summary>
    /// <returns>The diagnostics record.</returns>
    /// <exception cref="InvalidOperationException">The loaded wasmtime binding is not the one this controller was built against (the message names both versions), or the native library is not the pinned one (the message names both hashes).</exception>
    public static VerifyRuntimeDiagnosticsData Build()
    {
        // The versions below are facts of the process; the host's guards make the binding the one
        // this controller was built against and the native library the pinned one, and the
        // engine proves the library loads. A module whose wasmtime is another or cannot load fails
        // the diagnostic with the reason instead of reporting a version it cannot run.
        WasmHostBinding.ThrowIfUnavailable();
        ProveTheHostStarts();

        var root = VerifyRuntimeLocator.Locate();

        var runtimes = new List<VerifyRuntimeInfoData>();
        foreach (var id in VerifyRuntimeCatalog.KnownIds)
        {
            var descriptor = VerifyRuntimeCatalog.Find(id)!;
            var available = root != null && VerifyRuntimeCatalog.Resolve(root, id, out _) != null;
            runtimes.Add(new VerifyRuntimeInfoData
            {
                RuntimeId = id,
                Sha256 = descriptor.Sha256,
                Available = available
            });
        }

        return new VerifyRuntimeDiagnosticsData
        {
            RuntimeTarget = RuntimeTarget(),
            WasmtimeVersion = VerifyEngineVersion.Current,
            RuntimesRootFound = root != null,
            Runtimes = runtimes
        };
    }

    /// <summary>OS and architecture as the diagnostics name them, e.g. <c>linux-x64</c>.</summary>
    public static string RuntimeTarget()
    {
        var os = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "macos"
            : "unknown";
        return $"{os}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
    }

    /// <summary>
    /// Makes and drops one wasm host: the native resolution, the natives guard, the engine. Kept
    /// out of line, so that the binding's members are looked up after the guard has passed, not
    /// when the caller is compiled.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ProveTheHostStarts()
    {
        using (new WasmHost())
        {
        }
    }
}
