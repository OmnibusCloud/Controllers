using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// The node-side runtime registry: the known (pinned) runtime set, resolved against a
/// runtimes root directory with content-hash verification. Verification results are
/// memoized per absolute path for the process lifetime — runtime files are immutable
/// by contract (they are hash-pinned), so one check per process is enough.
/// </summary>
public static class VerifyRuntimeCatalog
{
    #region Constants

    public const string PYTHON_3_14_6 = "python-3.14.6";
    public const string QUICKJS_0_15_1 = "quickjs-0.15.1";

    #endregion

    #region Fields

    private static readonly IReadOnlyDictionary<string, VerifyRuntimeDescriptor> s_known =
        new Dictionary<string, VerifyRuntimeDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            [PYTHON_3_14_6] = new()
            {
                Id = PYTHON_3_14_6,
                Language = VerifyRuntimeLanguage.Python,
                ModulePath = Path.Combine("python-3.14.6", "python.wasm"),
                RootDir = "python-3.14.6",
                Sha256 = "cd71a34d8467882a4ad0e6a5e64509d68975ed6bd6831ca549bcd0517ac14655",
                Argv0 = "python.wasm"
            },
            [QUICKJS_0_15_1] = new()
            {
                Id = QUICKJS_0_15_1,
                Language = VerifyRuntimeLanguage.JavaScript,
                ModulePath = "qjs-wasi.wasm",
                RootDir = null,
                Sha256 = "b4071ef2fbb2bb693c0bbcfc07cb9d28639fd9cea2fd986824a57aeac929817b",
                Argv0 = "qjs"
            }
        };

    private static readonly ConcurrentDictionary<string, bool> s_hashChecks = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Functions

    /// <summary>All runtime ids this build knows how to pin.</summary>
    public static IEnumerable<string> KnownIds => s_known.Keys;

    public static VerifyRuntimeDescriptor? Find(string runtimeId)
    {
        return s_known.GetValueOrDefault(runtimeId);
    }

    /// <summary>
    /// Resolves a runtime id to verified on-disk paths. Returns null (with a reason)
    /// when the id is unknown, files are missing, or the module fails its hash pin —
    /// all of which map to the RuntimeUnavailable verdict, never to an exception.
    /// </summary>
    public static VerifyResolvedRuntime? Resolve(string runtimesRoot, string runtimeId, out string? unavailableReason)
    {
        var descriptor = Find(runtimeId);
        if (descriptor == null)
        {
            unavailableReason = $"unknown runtime id '{runtimeId}' (known: {string.Join(", ", KnownIds)})";
            return null;
        }

        var modulePath = Path.GetFullPath(Path.Combine(runtimesRoot, descriptor.ModulePath));
        if (!File.Exists(modulePath))
        {
            unavailableReason = $"runtime module not found at '{modulePath}'";
            return null;
        }

        var rootPath = descriptor.RootDir == null
            ? null
            : Path.GetFullPath(Path.Combine(runtimesRoot, descriptor.RootDir));
        if (rootPath != null && !Directory.Exists(rootPath))
        {
            unavailableReason = $"runtime root not found at '{rootPath}'";
            return null;
        }

        if (!s_hashChecks.GetOrAdd(modulePath, path => HashMatches(path, descriptor.Sha256)))
        {
            unavailableReason = $"runtime module at '{modulePath}' failed its SHA-256 pin";
            return null;
        }

        unavailableReason = null;
        return new VerifyResolvedRuntime(descriptor, modulePath, rootPath);
    }

    private static bool HashMatches(string path, string expectedSha256)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        return actual == expectedSha256;
    }

    #endregion
}

/// <summary>A runtime that passed resolution: descriptor plus verified absolute paths.</summary>
public sealed record VerifyResolvedRuntime(VerifyRuntimeDescriptor Descriptor, string ModulePath, string? RootPath);
