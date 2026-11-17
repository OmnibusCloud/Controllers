using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Wasm;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Builds a C/C++ task's program on the node, inside the sandbox: LLVM for WASI is one module, and
/// WASI cannot start processes, so every step is its own run of it. Each source (.c as C17, .cc,
/// .cpp, .cxx as C++20 without exceptions) is compiled by <c>clang -cc1</c> with the sources as
/// the guest's "/" and the sysroot as "/usr", its object written to stdout; the objects are put
/// in a fresh folder, the guest's "/in"; <c>wasm-ld</c> writes the program module to stdout.
/// Nothing is writable for the tools, and every folder they see has a table of its names, so a
/// header is found under the exact name it has and under no other, on every node. The command
/// lines are fixed (the driver's plan, captured once); every step runs under one compile budget
/// for the task, apart from the program's own limits. Fuel is what ends a build that is too
/// large, the same on every node; a step's product is capped where it is written. A file's time
/// reads as zero in the sandbox, so <c>__TIMESTAMP__</c> is the same text everywhere, like
/// <c>__DATE__</c> and <c>__TIME__</c>.
/// </summary>
internal static class VerifyClangBuild
{
    #region Constants

    /// <summary>The fuel all the steps of one task's build may use together.</summary>
    public const long COMPILE_FUEL = 100_000_000_000;

    /// <summary>
    /// The wall-clock backstop of a build, all its steps together. It gives no verdict: a build
    /// the clock ended is a fault of the node, and the task is sent out once more.
    /// </summary>
    public const int COMPILE_WALL_MS = 600_000;

    /// <summary>The memory cap of one step (a C++ compile of a task-sized program takes under 100 MiB).</summary>
    public const long STEP_MEMORY_BYTES = 1L << 30;

    /// <summary>The largest object or program a step may produce; the write that passes it ends the step.</summary>
    public const int PRODUCT_LIMIT_BYTES = 64 << 20;

    /// <summary>How much of a step's diagnostics a failed build reports.</summary>
    public const int DIAGNOSTICS_LIMIT_BYTES = 64 * 1024;

    private const string SOURCES = "/";

    /// <summary>The path the compiler and the linker see the sysroot under.</summary>
    public const string SYSROOT = "/usr";
    private const string OBJECTS = "/in";

    private static readonly string[] CC1 =
    [
        "-cc1", "-triple", "wasm32-unknown-wasip1", "-O2", "-emit-obj", "-disable-free", "-clear-ast-before-backend",
        "-mrelocation-model", "static", "-mframe-pointer=none", "-ffp-contract=on", "-fno-rounding-math",
        "-mconstructor-aliases", "-target-cpu", "generic", "-fvisibility=hidden", "-debugger-tuning=gdb",
        "-fdebug-compilation-dir=/", "-fcoverage-compilation-dir=/", "-resource-dir", SYSROOT, "-isysroot", SYSROOT
    ];

    private static readonly string[] C_INCLUDES =
    [
        "-internal-isystem", "/usr/include", "-internal-isystem", "/usr/include/wasm32-wasip1", "-internal-isystem", "/usr/include",
        "-std=c17"
    ];

    private static readonly string[] CXX_INCLUDES =
    [
        "-internal-isystem", "/usr/include/wasm32-wasip1/c++/v1", "-internal-isystem", "/usr/include/c++/v1",
        "-internal-isystem", "/usr/include", "-internal-isystem", "/usr/include/wasm32-wasip1", "-internal-isystem", "/usr/include",
        "-fdeprecated-macro", "-std=c++20"
    ];

    private static readonly string[] CC1_TAIL =
    [
        "-ferror-limit", "19", "-fgnuc-version=4.2.1", "-fskip-odr-check-in-gmf", "-vectorize-loops", "-vectorize-slp"
    ];

    // The program's stack: 8 MiB as on Linux, not wasm-ld's 64 KiB (a local array of a hundred
    // thousand ints would not fit), and below the data, so an overflow leaves the memory and
    // traps instead of writing over the program's globals.
    private static readonly string[] LINK_HEAD =
    [
        "wasm-ld", "-m", "wasm32", "--stack-first", "-z", "stack-size=8388608",
        "-L/usr/lib/wasm32-unknown-wasip1", "-L/usr/lib/wasm32-wasip1", "/usr/lib/wasm32-wasip1/crt1-command.o"
    ];

    private static readonly string[] LINK_TAIL =
    [
        "-lc++", "-lc++abi", "-lc", "/usr/lib/wasm32-unknown-wasip1/libclang_rt.builtins.a", "-o", "-"
    ];

    private static readonly HashSet<string> C_EXTENSIONS = new(StringComparer.Ordinal) { ".c" };
    private static readonly HashSet<string> CXX_EXTENSIONS = new(StringComparer.Ordinal) { ".cc", ".cpp", ".cxx", ".c++" };

    #endregion

    #region Functions

    /// <summary>
    /// Builds the task's program.
    /// </summary>
    /// <param name="tools">Runs the compiler and the linker.</param>
    /// <param name="llvm">LLVM's compiled module.</param>
    /// <param name="sysroot">The sysroot, the guest's /usr, with the table of its names.</param>
    /// <param name="task">The task.</param>
    /// <param name="sources">The folder holding the task's sources as files, the guest's /, with the table of their names.</param>
    /// <param name="workDir">A host folder for the objects and the program, made here.</param>
    /// <param name="cancellationToken">Checked before every step; a step already started ends within the budget.</param>
    /// <returns>The program module's bytes, or the reason there is none (diagnostics as the author reads them).</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled between two steps.</exception>
    /// <exception cref="VerifyNodeFaultException">The clock, not fuel, ended a step, or a tool did not start: a fault of the node.</exception>
    public static (byte[]? Program, string? Failure) Build(VerifyToolRunner tools, WasmModule llvm, WasmHostPreopen sysroot,
        VerifyTaskData task, WasmHostPreopen sources, string workDir, CancellationToken cancellationToken)
    {
        var units = task.Sources
            .Select(source => (source.Name, Kind: LanguageOf(source.Name)))
            .Where(unit => unit.Kind != null)
            .OrderBy(unit => unit.Name, StringComparer.Ordinal)
            .ToList();
        if (units.Count == 0)
            return (null, "no C or C++ source to compile (.c, .cc, .cpp, .cxx)");

        var objects = Path.Combine(workDir, "in");
        Directory.CreateDirectory(objects);

        var fuelLeft = COMPILE_FUEL;
        var wallLeft = COMPILE_WALL_MS;
        var objectFiles = new List<string>();

        for (var i = 0; i < units.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (name, kind) = units[i];
            var objectName = $"{i}.o";
            var argv = CompileArgv(name, kind!);
            var step = Step(tools, llvm, argv, [sources, sysroot], Path.Combine(objects, objectName), workDir, ref fuelLeft, ref wallLeft);
            if (step != null)
                return (null, step);

            objectFiles.Add(objectName);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var program = Path.Combine(workDir, "program.wasm");
        string[] link = ["llvm", .. LINK_HEAD, .. objectFiles.Select(file => $"{OBJECTS}/{file}"), .. LINK_TAIL];
        var objectFolder = new WasmHostPreopen(objects, OBJECTS, Names: new WasmHostNames(objectFiles));
        var linked = Step(tools, llvm, link, [sysroot, objectFolder], program, workDir, ref fuelLeft, ref wallLeft);
        if (linked != null)
            return (null, linked);

        return (File.ReadAllBytes(program), null);
    }

    /// <summary>
    /// The compile step's argv for one source: the driver's plan for it, fixed.
    /// </summary>
    /// <param name="name">The source's name, relative to the sources' folder.</param>
    /// <param name="kind">"c" or "c++".</param>
    /// <returns>argv, argv[0] included.</returns>
    internal static string[] CompileArgv(string name, string kind)
    {
        var file = name[(name.LastIndexOf('/') + 1)..];
        var stem = file.Contains('.') ? file[..file.LastIndexOf('.')] : file;
        var includes = kind == "c" ? C_INCLUDES : CXX_INCLUDES;

        return ["llvm", kind == "c" ? "clang" : "clang++", .. CC1, .. includes, .. CC1_TAIL,
            "-main-file-name", file, "-dumpdir", stem + "-", "-o", "-", "-x", kind, name];
    }

    private static string? LanguageOf(string name)
    {
        var dot = name.LastIndexOf('.');
        var extension = dot < 0 ? "" : name[dot..].ToLowerInvariant();
        if (C_EXTENSIONS.Contains(extension))
            return "c";

        return CXX_EXTENSIONS.Contains(extension) ? "c++" : null;
    }

    private static string? Step(VerifyToolRunner tools, WasmModule llvm, IReadOnlyList<string> argv,
        IReadOnlyList<WasmHostPreopen> folders, string product, string workDir, ref long fuelLeft, ref int wallLeft)
    {
        var outcome = tools.Run(llvm, argv, folders, product, PRODUCT_LIMIT_BYTES, DIAGNOSTICS_LIMIT_BYTES, workDir, fuelLeft, Math.Max(1, wallLeft), STEP_MEMORY_BYTES);
        fuelLeft -= outcome.Fuel;
        wallLeft -= outcome.WallMs;

        var tool = argv[1];
        switch (outcome.Verdict)
        {
            case WasmHostVerdict.Exited when outcome.ExitCode == 0:
                return null;
            case WasmHostVerdict.Exited:
                return outcome.Diagnostics is { Length: > 0 } text ? text : $"{tool} exited with {outcome.ExitCode}";
            case WasmHostVerdict.OutOfFuel:
                return $"the build did not finish within its budget ({COMPILE_FUEL / 1_000_000_000} G fuel for all its steps)";
            case WasmHostVerdict.OutputLimit:
                return $"{tool} produced more than {PRODUCT_LIMIT_BYTES >> 20} MiB";
            case WasmHostVerdict.Trap:
                return $"{tool} stopped: {outcome.Trap}";
            case WasmHostVerdict.EngineError:
                return $"{tool} stopped: the engine refused one of its calls";
            case WasmHostVerdict.Interrupted:
                // The clock gives no verdict: the node was too slow for this build, or too busy.
                throw new VerifyNodeFaultException(VerifyFault.WALL_CLOCK);
            default:
                // The pinned tool did not start: nothing the sources could have caused.
                throw new VerifyNodeFaultException(VerifyFault.NODE_FAULT);
        }
    }

    #endregion
}
