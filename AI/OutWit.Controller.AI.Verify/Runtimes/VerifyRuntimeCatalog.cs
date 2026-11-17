using System.Collections.Concurrent;
using System.Security.Cryptography;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// The node-side runtime registry: the known (pinned) runtime set, resolved against a
/// runtimes root directory with content-hash verification of the module, of every layer
/// and of every library tree. A check that passed is remembered per absolute path for the
/// process lifetime: runtime files are immutable by contract (they are hash-pinned), so one
/// good check per process is enough. A check that failed is not remembered: a file hashed while
/// its archive was still being unpacked is good a moment later, and a node must not go on
/// refusing work for the rest of its life because of that moment.
/// <para>
/// A reason this class gives for a runtime that does not resolve is fixed words without a path:
/// it may travel. Where on the node the trouble is goes into the detail, which is for the
/// node's own log only.
/// </para>
/// </summary>
public static class VerifyRuntimeCatalog
{
    #region Constants

    /// <summary>CPython 3.14.6 (WASI) with the standard library as a bytecode layer.</summary>
    public const string PYTHON_3_14_6 = "python-3.14.6";

    /// <summary>CPython 3.14.6 with the sympy 1.14.0 + mpmath 1.4.1 layer on PYTHONPATH.</summary>
    public const string PYTHON_3_14_6_SYMPY_1_14_0 = "python-3.14.6-sympy-1.14.0";

    /// <summary>
    /// CPython 3.14.6 with numpy 2.5.4 built in, both built for WASI by the controller's
    /// toolchain (toolchain/python-numpy): a start-up snapshot with numpy imported, its own
    /// standard library layer and numpy's Python package as a layer on PYTHONPATH.
    /// </summary>
    public const string PYTHON_3_14_6_NUMPY_2_5_4 = "python-3.14.6-numpy-2.5.4";

    /// <summary>
    /// QuickJS-NG 0.15.1, JavaScript, built for WASI by the controller's toolchain: its C stack
    /// below the data and its stack limit on, so a recursion too deep is a RangeError.
    /// </summary>
    public const string QUICKJS_0_15_1 = "quickjs-0.15.1";

    /// <summary>
    /// Ruby 3.4.1 (WASI, the ruby.wasm 2.10.1 build) with the standard library and the
    /// bundled gems base64, csv, matrix and prime; no RubyGems.
    /// </summary>
    public const string RUBY_3_4_1 = "ruby-3.4.1";

    /// <summary>
    /// Lua 5.4.8, the stock interpreter built for WASI by the controller's toolchain (errors
    /// by an asyncify unwind, no exception handling); the task's sources are its files.
    /// </summary>
    public const string LUA_5_4_8 = "lua-5.4.8";

    /// <summary>
    /// TypeScript: the JavaScript runtime's QuickJS with sucrase 3.35.1 stripping the types
    /// (bundled by the controller's toolchain); no type check.
    /// </summary>
    public const string TYPESCRIPT_SUCRASE_3_35_1 = "typescript-sucrase-3.35.1";

    /// <summary>
    /// C17 and C++20 (no exceptions) compiled on the node by clang 22.1.0 for WASI (YoWASP's build)
    /// with wasi-libc and libc++; the program runs as a raw WASI module.
    /// </summary>
    public const string CLANG_22_1_0 = "clang-22.1.0";

    /// <summary>The raw WASI runtime: the task's entry source is the command module itself.</summary>
    public const string WASI = "wasi";

    private const string PYTHON_DIR = "python-3.14.6";
    private const string PYTHON_SHA256 = "cd71a34d8467882a4ad0e6a5e64509d68975ed6bd6831ca549bcd0517ac14655";

    // The numpy runtime is its own folder (its own data asset): the snapshot is bound to the bytes
    // of the two layers it was made with, so they travel and are pinned together.
    private const string NUMPY_DIR = "python-3.14.6-numpy-2.5.4";
    private const string NUMPY_SHA256 = "3cfedd8bb07f24e04dd9783e86985b236fd06955d2d938541045564467d05a1e";

    private const string RUBY_DIR = "ruby-3.4.1";
    private const string RUBY_GEMS = "/usr/local/lib/ruby/gems/3.4.0/gems/";

    private const string LUA_DIR = "lua-5.4.8";

    private const string QUICKJS_MODULE = "qjs-wasi.wasm";
    private const string QUICKJS_SHA256 = "3629434c55636670bad9df38c7979e24213559261497c846c0aa11e1c9d39bef";
    private const string TYPESCRIPT_DIR = "typescript-sucrase-3.35.1";

    private const string CLANG_DIR = "clang-22.1.0";

    /// <summary>The reason of a runtime id the catalog does not have.</summary>
    public const string UNKNOWN_RUNTIME = "the runtime id is not known to this controller";

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

    private static readonly VerifyRuntimeLayer NUMPY_STDLIB = new(
        Path.Combine(NUMPY_DIR, "lib", "python314.zip"),
        "5d02f04016569aeba0f6ae96130728fa875abb438ba72d4014132e2a542f64a4",
        null);

    private static readonly VerifyRuntimeLayer NUMPY_PACKAGE = new(
        Path.Combine(NUMPY_DIR, "lib", "numpy-2.5.4.zip"),
        "bb3425211b21ac282c3b2602711568cb30b7c173cc67032f17b270fed0acaf35",
        "/lib/numpy-2.5.4.zip");

    // Ruby loads its library from files, so the library is a tree verify-assets cuts out of
    // the release archive unchanged. The tree is the guest's whole file system: a program
    // can read nothing that is not under this pin.
    private static readonly VerifyRuntimeTree RUBY_ROOT = new(
        Path.Combine(RUBY_DIR, "root"),
        "8d91587b3961fc4e96bdad55c9d8309ae1b9434551dc1e5f3e885e7ee9b358c7");

    // sucrase's transform bundled by the toolchain (toolchain/typescript), the script QuickJS
    // loads before a TypeScript task's bootstrap.
    private static readonly VerifyRuntimeLayer TYPESCRIPT_SUCRASE = new(
        Path.Combine(TYPESCRIPT_DIR, "sucrase.js"),
        "3390a6c1f791844cb001ec1686ae712e7b29e5d8283b7a18f688a1bc03e4b506",
        null);

    // The sysroot the compiler reads at /usr (wasi-libc, libc++, libc++abi, compiler-rt, clang's
    // headers), cut unchanged from YoWASP's package and pinned as a tree.
    private static readonly VerifyRuntimeTree CLANG_SYSROOT = new(
        Path.Combine(CLANG_DIR, "usr"),
        "ce89a7facc82bc2249bb9ed3fac69f1bccbe43c47ec026e40ebe46a8b21f65a5");

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
            [PYTHON_3_14_6_NUMPY_2_5_4] = new()
            {
                Id = PYTHON_3_14_6_NUMPY_2_5_4,
                Language = VerifyRuntimeLanguage.Python,
                ModulePath = Path.Combine(NUMPY_DIR, "python.wasm"),
                RootDir = NUMPY_DIR,
                Sha256 = NUMPY_SHA256,
                Argv0 = "python.wasm",
                Layers = [NUMPY_STDLIB, NUMPY_PACKAGE],
                Notes =
                [
                    "numpy's default integer is 32-bit (wasm32): np.arange(1_000_000).sum() wraps to 1783293664; pass dtype=np.int64 for 64-bit integers.",
                    "No BLAS (numpy's own lapack_lite), no threads, no SIMD.",
                    "np.longdouble is IEEE binary128 in software, not x86's 80-bit extended.",
                    "Floating-point arithmetic raises no warnings (1.0 / 0 is inf silently); numpy's own errors (a singular matrix, integer division by zero) are raised."
                ]
            },
            [QUICKJS_0_15_1] = new()
            {
                Id = QUICKJS_0_15_1,
                Language = VerifyRuntimeLanguage.JavaScript,
                ModulePath = QUICKJS_MODULE,
                RootDir = null,
                Sha256 = QUICKJS_SHA256,
                Argv0 = "qjs"
            },
            [TYPESCRIPT_SUCRASE_3_35_1] = new()
            {
                Id = TYPESCRIPT_SUCRASE_3_35_1,
                Language = VerifyRuntimeLanguage.TypeScript,
                // The JavaScript runtime's module; the transpiler is a script in the read-only root.
                ModulePath = QUICKJS_MODULE,
                RootDir = TYPESCRIPT_DIR,
                Sha256 = QUICKJS_SHA256,
                Argv0 = "qjs",
                Layers = [TYPESCRIPT_SUCRASE],
                Notes = ["The types are stripped by sucrase, not checked: a program with type errors runs."]
            },
            [RUBY_3_4_1] = new()
            {
                Id = RUBY_3_4_1,
                Language = VerifyRuntimeLanguage.Ruby,
                ModulePath = Path.Combine(RUBY_DIR, "ruby.wasm"),
                RootDir = RUBY_ROOT.Path,
                Sha256 = "348305ee0b4e4cdb84ec169223e33721899548577a42a421725b71e481afff11",
                Argv0 = "ruby",
                Trees = [RUBY_ROOT],
                // The bundled gems a program may require: RubyGems is off (it would scan the
                // file system at every start), so their library folders are named here.
                LoadPaths =
                [
                    RUBY_GEMS + "base64-0.2.0/lib",
                    RUBY_GEMS + "csv-3.3.2/lib",
                    RUBY_GEMS + "matrix-0.4.2/lib",
                    RUBY_GEMS + "prime-0.1.3/lib"
                ]
            },
            [LUA_5_4_8] = new()
            {
                Id = LUA_5_4_8,
                Language = VerifyRuntimeLanguage.Lua,
                ModulePath = Path.Combine(LUA_DIR, "lua.wasm"),
                RootDir = null,
                // Lua's library is in the module; the guest's file system is the task's sources.
                SourcesAsRoot = true,
                Sha256 = "5b953864a7d270d34c2c7360f90bd5e5d9c2196776d6af185dfb12fd29558baf",
                Argv0 = "lua"
            },
            [CLANG_22_1_0] = new()
            {
                Id = CLANG_22_1_0,
                Language = VerifyRuntimeLanguage.C,
                ModulePath = Path.Combine(CLANG_DIR, "llvm.wasm"),
                // The sysroot: the compiler's /usr. The sources are the compiler's "/", written per task.
                RootDir = CLANG_SYSROOT.Path,
                SourcesAsRoot = true,
                Sha256 = "24fbed474c7b5b4968fd73fc4827440b93fb351c1b6264516130300eff3e7bf5",
                Argv0 = "llvm",
                Trees = [CLANG_SYSROOT],
                Notes =
                [
                    "C++ without exceptions: try and throw are a compile error.",
                    "A 32-bit target: int, long and pointers are 32 bits, long long is 64.",
                    "A recursion reaches about 100,000 to 160,000 levels (the engine's 8 MiB stack, as on Linux)."
                ]
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

    private static readonly ConcurrentDictionary<string, bool> s_verified = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, WasmHostNames> s_names = new(StringComparer.Ordinal);

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
    /// module fails its hash pin. A task-supplied runtime needs nothing on disk and resolves
    /// without a root.
    /// </summary>
    /// <param name="runtimesRoot">The runtimes root, or null when the node has none.</param>
    /// <param name="runtimeId">The runtime id a task names.</param>
    /// <param name="unavailableReason">Why the runtime is unavailable, when it is: fixed words, no path.</param>
    /// <returns>The resolved runtime, or null.</returns>
    public static VerifyResolvedRuntime? Resolve(string? runtimesRoot, string runtimeId, out string? unavailableReason)
    {
        return Resolve(runtimesRoot, runtimeId, out unavailableReason, out _);
    }

    /// <summary>
    /// Resolves a runtime id to verified on-disk paths, and says where on the node the trouble
    /// is when there is some.
    /// </summary>
    /// <param name="runtimesRoot">The runtimes root, or null when the node has none.</param>
    /// <param name="runtimeId">The runtime id a task names.</param>
    /// <param name="unavailableReason">Why the runtime is unavailable, when it is: fixed words, no path.</param>
    /// <param name="nodeDetail">The file or folder the reason is about, for the node's own log; never sent anywhere.</param>
    /// <returns>The resolved runtime, or null.</returns>
    public static VerifyResolvedRuntime? Resolve(string? runtimesRoot, string runtimeId, out string? unavailableReason, out string? nodeDetail)
    {
        nodeDetail = null;

        var descriptor = Find(runtimeId);
        if (descriptor == null)
        {
            unavailableReason = UNKNOWN_RUNTIME;
            return null;
        }

        if (descriptor.TaskSupplied)
        {
            unavailableReason = null;
            return new VerifyResolvedRuntime(descriptor, "", null);
        }

        if (runtimesRoot == null)
        {
            unavailableReason = "the runtimes are not installed";
            return null;
        }

        var modulePath = Path.GetFullPath(Path.Combine(runtimesRoot, descriptor.ModulePath));
        if (!File.Exists(modulePath))
        {
            unavailableReason = "the runtime's module is missing";
            nodeDetail = modulePath;
            return null;
        }

        var rootPath = descriptor.RootDir == null
            ? null
            : Path.GetFullPath(Path.Combine(runtimesRoot, descriptor.RootDir));
        if (rootPath != null && !Directory.Exists(rootPath))
        {
            unavailableReason = "the runtime's folder is missing";
            nodeDetail = rootPath;
            return null;
        }

        if (!Verified(modulePath, path => HashMatches(path, descriptor.Sha256)))
        {
            unavailableReason = "the runtime's module failed its SHA-256 pin";
            nodeDetail = modulePath;
            return null;
        }

        foreach (var layer in descriptor.Layers)
        {
            var layerPath = Path.GetFullPath(Path.Combine(runtimesRoot, layer.Path));
            if (!File.Exists(layerPath))
            {
                unavailableReason = "a layer of the runtime is missing";
                nodeDetail = layerPath;
                return null;
            }

            if (!Verified(layerPath, path => HashMatches(path, layer.Sha256)))
            {
                unavailableReason = "a layer of the runtime failed its SHA-256 pin";
                nodeDetail = layerPath;
                return null;
            }
        }

        foreach (var tree in descriptor.Trees)
        {
            var treePath = Path.GetFullPath(Path.Combine(runtimesRoot, tree.Path));
            if (!Directory.Exists(treePath))
            {
                unavailableReason = "the runtime's library is missing";
                nodeDetail = treePath;
                return null;
            }

            if (!Verified(treePath, path => TreeMatches(path, tree.Sha256)))
            {
                unavailableReason = "the runtime's library failed its SHA-256 pin (a file was changed, added or removed)";
                nodeDetail = treePath;
                return null;
            }
        }

        unavailableReason = null;
        return new VerifyResolvedRuntime(descriptor, modulePath, rootPath) { RootNames = rootPath == null ? null : NamesOf(runtimesRoot, rootPath, descriptor) };
    }

    /// <summary>
    /// The names a guest may reach in a runtime's folder: its pinned layers and every file of its
    /// pinned library trees, by their paths relative to the folder. Nothing else that may lie in
    /// the folder exists for a guest. Read once a folder: what is pinned does not change.
    /// </summary>
    private static WasmHostNames NamesOf(string runtimesRoot, string rootPath, VerifyRuntimeDescriptor descriptor)
    {
        return s_names.GetOrAdd($"{rootPath}|{descriptor.Id}", _ =>
        {
            var files = new List<string>();

            foreach (var layer in descriptor.Layers)
                AddIfInside(files, rootPath, Path.GetFullPath(Path.Combine(runtimesRoot, layer.Path)));

            foreach (var tree in descriptor.Trees)
            {
                var treePath = Path.GetFullPath(Path.Combine(runtimesRoot, tree.Path));
                foreach (var file in Directory.EnumerateFiles(treePath, "*", SearchOption.AllDirectories))
                    AddIfInside(files, rootPath, file);
            }

            return new WasmHostNames(files);
        });
    }

    private static void AddIfInside(List<string> files, string rootPath, string file)
    {
        var relative = Path.GetRelativePath(rootPath, file);
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
            return;

        files.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
    }

    /// <summary>Checks a path, remembering a check that passed and nothing else.</summary>
    private static bool Verified(string path, Func<string, bool> check)
    {
        if (s_verified.ContainsKey(path))
            return true;

        if (!check(path))
            return false;

        s_verified[path] = true;
        return true;
    }

    private static bool HashMatches(string path, string expectedSha256)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
            return actual == expectedSha256;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be read now (still being written) cannot be vouched for now.
            return false;
        }
    }

    private static bool TreeMatches(string path, string expectedSha256)
    {
        try
        {
            return VerifyRuntimeTreeHash.Compute(path) == expectedSha256;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A tree that cannot be read whole cannot be vouched for.
            return false;
        }
    }

    #endregion
}

/// <summary>
/// A runtime that passed resolution: descriptor plus verified absolute paths (both empty
/// for a task-supplied runtime).
/// </summary>
/// <param name="Descriptor">The pinned runtime.</param>
/// <param name="ModulePath">The module file, verified.</param>
/// <param name="RootPath">The folder a guest sees as its root, or null when the runtime has none.</param>
public sealed record VerifyResolvedRuntime(VerifyRuntimeDescriptor Descriptor, string ModulePath, string? RootPath)
{
    /// <summary>The names a guest may reach in the root folder: the pinned files and nothing else. Null when the runtime has no folder.</summary>
    internal WasmHostNames? RootNames { get; init; }
}
