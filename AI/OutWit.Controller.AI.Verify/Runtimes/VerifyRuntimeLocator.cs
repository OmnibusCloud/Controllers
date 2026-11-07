using System.Reflection;

namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// Finds the on-disk root of the pinned language-runtime modules on a node. Resolution order:
/// the WITAI_VERIFY_RUNTIMES override (tests and non-standard layouts), then the "wasm"
/// folder next to the controller assembly, where the module's data asset extracts them.
/// The folder is deliberately not called "runtimes": that name is the .NET convention for
/// RID-specific native assets (runtimes/&lt;rid&gt;/native/), which the module also carries
/// for wasmtime itself and which the plugin load context resolves through the deps.json.
/// </summary>
public static class VerifyRuntimeLocator
{
    public const string OverrideEnvVar = "WITAI_VERIFY_RUNTIMES";

    private const string RuntimesFolderName = "wasm";

    public static string? Locate()
    {
        var overridePath = Environment.GetEnvironmentVariable(OverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath) && Directory.Exists(overridePath))
            return Path.GetFullPath(overridePath);

        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (assemblyDir == null)
            return null;

        var candidate = Path.Combine(assemblyDir, RuntimesFolderName);
        return Directory.Exists(candidate) ? candidate : null;
    }
}
