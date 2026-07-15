using System.Runtime.InteropServices;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// Builds a node's sandbox runtime inventory: the wasmtime host version and, for each
/// known runtime, whether its module is present and hash-verified on this node.
/// </summary>
public static class VerifyRuntimeDiagnosticsBuilder
{
    public static VerifyRuntimeDiagnosticsData Build()
    {
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

    private static string RuntimeTarget()
    {
        var os = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "macos"
            : "unknown";
        return $"{os}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
    }
}
