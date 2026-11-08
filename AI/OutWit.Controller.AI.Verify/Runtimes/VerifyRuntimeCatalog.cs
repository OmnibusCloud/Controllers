using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// The node-side runtime registry: the known (pinned) runtime set, resolved against a
/// runtimes root directory with content-hash verification of the module and of every
/// layer. Verification results are memoized per absolute path for the process lifetime —
/// runtime files are immutable by contract (they are hash-pinned), so one check per
/// process is enough.
/// </summary>
public static class VerifyRuntimeCatalog
{
    #region Constants

    /// <summary>CPython 3.14.6 (WASI) with the standard library as a bytecode layer.</summary>
    public const string PYTHON_3_14_6 = "python-3.14.6";

    /// <summary>CPython 3.14.6 with the sympy 1.14.0 + mpmath 1.4.1 layer on PYTHONPATH.</summary>
    public const string PYTHON_3_14_6_SYMPY_1_14_0 = "python-3.14.6-sympy-1.14.0";

    /// <summary>QuickJS-NG 0.15.1 (WASI), JavaScript.</summary>
    public const string QUICKJS_0_15_1 = "quickjs-0.15.1";

    /// <summary>The raw WASI runtime: the task's entry source is the command module itself.</summary>
    public const string WASI = "wasi";

    private const string PYTHON_DIR = "python-3.14.6";
    private const string PYTHON_SHA256 = "cd71a34d8467882a4ad0e6a5e64509d68975ed6bd6831ca549bcd0517ac14655";

    #endregion

    #region Fields

    // The layers verify-assets builds (bytecode-only zips, reproducible byte for byte): the
    // standard library, which sys.path holds by default, and the sympy layer on PYTHONPATH.
    private static readonly VerifyRuntimeLayer PYTHON_STDLIB = new(
        Path.Combine(PYTHON_DIR, "lib", "python314.zip"),
        "c61e719af81d000e8b611a0908502517c42c3d91d74785f845d13bc6651ddf66",
        null);

    private static readonly VerifyRuntimeLayer PYTHON_SYMPY = new(
        Path.Combine(PYTHON_DIR, "lib", "sympy-1.14.0.zip"),
        "d15dce77126085b9e7c327474e0574975d62515a28ea1551bec2cc90079a2b2a",
        "/lib/sympy-1.14.0.zip");

    private static readonly IReadOnlyDictionary<string, VerifyRuntimeDescriptor> s_known =
        new Dictionary<string, VerifyRuntimeDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            [PYTHON_3_14_6] = new()
            {
                Id = PYTHON_3_14_6,
                Language = VerifyRuntimeLanguage.Python,
                ModulePath = Path.Combine(PYTHON_DIR, "python.wasm"),
                RootDir = PYTHON_DIR,
                Sha256 = PYTHON_SHA256,
                Argv0 = "python.wasm",
                Layers = [PYTHON_STDLIB]
            },
            [PYTHON_3_14_6_SYMPY_1_14_0] = new()
            {
                Id = PYTHON_3_14_6_SYMPY_1_14_0,
                Language = VerifyRuntimeLanguage.Python,
                ModulePath = Path.Combine(PYTHON_DIR, "python.wasm"),
                RootDir = PYTHON_DIR,
                Sha256 = PYTHON_SHA256,
                Argv0 = "python.wasm",
                Layers = [PYTHON_STDLIB, PYTHON_SYMPY]
            },
            [QUICKJS_0_15_1] = new()
            {
                Id = QUICKJS_0_15_1,
                Language = VerifyRuntimeLanguage.JavaScript,
                ModulePath = "qjs-wasi.wasm",
                RootDir = null,
                Sha256 = "b4071ef2fbb2bb693c0bbcfc07cb9d28639fd9cea2fd986824a57aeac929817b",
                Argv0 = "qjs"
            },
            [WASI] = new()
            {
                Id = WASI,
                Language = VerifyRuntimeLanguage.Wasi,
                TaskSupplied = true,
                ModulePath = "",
                RootDir = null,
                Sha256 = "",
                Argv0 = "main.wasm"
            }
        };

    private static readonly ConcurrentDictionary<string, bool> s_hashChecks = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Functions

    /// <summary>All runtime ids this build knows how to pin.</summary>
    public static IEnumerable<string> KnownIds => s_known.Keys;

    /// <summary>Looks a runtime id up in the catalog.</summary>
    /// <param name="runtimeId">The runtime id a task names.</param>
    /// <returns>The descriptor, or null for an unknown id.</returns>
    public static VerifyRuntimeDescriptor? Find(string runtimeId)
    {
        return s_known.GetValueOrDefault(runtimeId);
    }

    /// <summary>
    /// Resolves a runtime id to verified on-disk paths. Returns null (with a reason)
    /// when the id is unknown, the runtimes root is missing, files are missing, or the
    /// module fails its hash pin — all of which map to the RuntimeUnavailable verdict,
    /// never to an exception. A task-supplied runtime needs nothing on disk and resolves
    /// without a root.
    /// </summary>
    /// <param name="runtimesRoot">The runtimes root, or null when the node has none.</param>
    /// <param name="runtimeId">The runtime id a task names.</param>
    /// <param name="unavailableReason">Why the runtime is unavailable, when it is.</param>
    /// <returns>The resolved runtime, or null.</returns>
    public static VerifyResolvedRuntime? Resolve(string? runtimesRoot, string runtimeId, out string? unavailableReason)
    {
        var descriptor = Find(runtimeId);
        if (descriptor == null)
        {
            unavailableReason = $"unknown runtime id '{runtimeId}' (known: {string.Join(", ", KnownIds)})";
            return null;
        }

        if (descriptor.TaskSupplied)
        {
            unavailableReason = null;
            return new VerifyResolvedRuntime(descriptor, "", null);
        }

        if (runtimesRoot == null)
        {
            unavailableReason = $"runtimes root not found (set {VerifyRuntimeLocator.OverrideEnvVar} or stage the runtime archive)";
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

        foreach (var layer in descriptor.Layers)
        {
            var layerPath = Path.GetFullPath(Path.Combine(runtimesRoot, layer.Path));
            if (!File.Exists(layerPath))
            {
                unavailableReason = $"runtime layer not found at '{layerPath}'";
                return null;
            }

            if (!s_hashChecks.GetOrAdd(layerPath, path => HashMatches(path, layer.Sha256)))
            {
                unavailableReason = $"runtime layer at '{layerPath}' failed its SHA-256 pin";
                return null;
            }
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

/// <summary>
/// A runtime that passed resolution: descriptor plus verified absolute paths (both empty
/// for a task-supplied runtime).
/// </summary>
public sealed record VerifyResolvedRuntime(VerifyRuntimeDescriptor Descriptor, string ModulePath, string? RootPath);
