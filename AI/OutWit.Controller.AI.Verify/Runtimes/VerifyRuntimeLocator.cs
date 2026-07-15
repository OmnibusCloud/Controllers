using System.Reflection;

namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// Finds the on-disk root of the pinned runtime modules on a node. Resolution order:
/// the WITAI_VERIFY_RUNTIMES override (tests and non-standard layouts), then a
/// "runtimes" folder next to the controller assembly (where the archive stages them).
/// </summary>
public static class VerifyRuntimeLocator
{
    public const string OverrideEnvVar = "WITAI_VERIFY_RUNTIMES";

    private const string RuntimesFolderName = "runtimes";

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
