using System.Runtime.InteropServices;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// Builds the sandbox runtime inventory of the process it runs in: the wasmtime host version
/// and, for each known runtime, whether its module is present and hash-verified. A plain
/// script function evaluates on the host, so through <c>VerifyRuntimeDiagnostics</c> this
/// describes the server's module; a node's module is exercised by <c>Verify.ExecuteBatch</c>.
/// </summary>
public static class VerifyRuntimeDiagnosticsBuilder
{
    public static VerifyRuntimeDiagnosticsData Build()
    {
        // The version below is a managed fact; the engine makes the native library a proven one.
        // A module whose wasmtime cannot load fails the diagnostic with the loader's message
        // instead of reporting a version it cannot run.
        VerifyNativeLibrary.EnsureRegistered();
        using (new Wasmtime.Engine())
        {
        }

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
            WasmtimeVersion = typeof(Wasmtime.Engine).Assembly.GetName().Version?.ToString() ?? "unknown",
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
}
