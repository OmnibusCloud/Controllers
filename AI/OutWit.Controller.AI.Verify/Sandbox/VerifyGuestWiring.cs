using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// What a guest sees of the world: argv, environment and the read-only root — the parts of one
/// invocation's WASI configuration that are the task's (the host adds the stdio redirects),
/// built the same way for every node so a program sees the same world everywhere. The root is
/// the runtime's library, or for Lua the task's own sources as files; elsewhere the sources
/// travel inline (or are the module). No guest file system is writable: the strongest
/// containment posture, and no state survives a run.
/// </summary>
internal static class VerifyGuestWiring
{
    #region Constants

    private const string GUEST_ROOT = "/";

    #endregion

    #region Functions

    /// <summary>
    /// The guest's environment: empty, but for what pins the interpreter.
    /// </summary>
    /// <param name="runtime">The resolved runtime.</param>
    /// <returns>The variables, in order.</returns>
    public static IReadOnlyList<(string Name, string Value)> Environment(VerifyResolvedRuntime runtime)
    {
        var environment = new List<(string Name, string Value)>();

        // Pin the interpreter's environment for reproducibility: the prefix is "/" (no
        // landmark search, no directory scans, no "platform dependent libraries" warning),
        // the hash seed is fixed, no bytecode is written, and the layers that need it are
        // on PYTHONPATH (the standard library zip is on sys.path by itself).
        if (runtime.Descriptor.Language == VerifyRuntimeLanguage.Python)
        {
            environment.Add(("PYTHONHOME", GUEST_ROOT));
            environment.Add(("PYTHONHASHSEED", "0"));
            environment.Add(("PYTHONDONTWRITEBYTECODE", "1"));
            environment.Add(("PYTHONUNBUFFERED", "1"));

            var pythonPath = string.Join(':', runtime.Descriptor.Layers.Where(layer => layer.PythonPath != null).Select(layer => layer.PythonPath));
            if (pythonPath.Length > 0)
                environment.Add(("PYTHONPATH", pythonPath));
        }

        return environment;
    }

    /// <summary>
    /// The folders the guest sees: at most one, read-only, as "/".
    /// </summary>
    /// <param name="runtime">The resolved runtime.</param>
    /// <param name="sourceRoot">The folder holding the task's sources as files when the runtime takes them so (Lua), else null.</param>
    /// <returns>The preopened folders, in order.</returns>
    public static IReadOnlyList<WasmHostPreopen> Preopens(VerifyResolvedRuntime runtime, string? sourceRoot)
    {
        // The runtime root (the interpreter's library layers), read-only as "/"; for a runtime
        // whose file system is the task (Lua), the folder of its sources instead.
        if (runtime.Descriptor.SourcesAsRoot)
            return sourceRoot != null ? [new WasmHostPreopen(sourceRoot, GUEST_ROOT)] : [];

        return runtime.RootPath != null ? [new WasmHostPreopen(runtime.RootPath, GUEST_ROOT)] : [];
    }

    /// <summary>
    /// The guest's argv: the interpreter's inline-source form for Python, JavaScript and
    /// Ruby, the transpiler and a bootstrap holding the source for TypeScript, the entry
    /// file's name for Lua (its sources are files), the module's own name for
    /// a raw WASI task, then the run's arguments.
    /// </summary>
    /// <param name="runtime">The resolved runtime.</param>
    /// <param name="task">The task.</param>
    /// <param name="args">The run's arguments.</param>
    /// <returns>argv, argv[0] included.</returns>
    public static string[] BuildArgv(VerifyResolvedRuntime runtime, VerifyTaskData task, IReadOnlyList<string> args)
    {
        var entry = EntrySource(task);
        var code = entry?.Content ?? "";

        // v1 executes the entry source inline (no writable guest FS). Multi-file programs
        // are a planned extension (a writable preopen + materialized sources).
        var argv = new List<string> { runtime.Descriptor.Argv0 };
        switch (runtime.Descriptor.Language)
        {
            case VerifyRuntimeLanguage.Python:
                // -P: no '' on sys.path (nothing lists the guest root); -B: no bytecode writes.
                argv.Add("-P");
                argv.Add("-B");
                argv.Add("-c");
                argv.Add(code);
                break;
            case VerifyRuntimeLanguage.JavaScript:
                argv.Add("-e");
                argv.Add(code);
                // qjs reads options until "--": without it an argument such as "-x" was taken
                // for one of its own and the program never ran.
                argv.Add("--");
                break;
            case VerifyRuntimeLanguage.Ruby:
                // No RubyGems: it would scan the guest file system at every start, and a
                // program has no gems to install anyway. The bundled gems' library folders
                // are named instead, so require finds them like any other library.
                argv.Add("--disable=gems");
                foreach (var loadPath in runtime.Descriptor.LoadPaths)
                {
                    argv.Add("-I");
                    argv.Add(loadPath);
                }

                argv.Add("-e");
                argv.Add(code);
                // Everything after "--" is the program's (ARGV), even when it starts with a dash.
                argv.Add("--");
                break;
            case VerifyRuntimeLanguage.TypeScript:
                // sucrase first, then the bootstrap module that strips the types and runs the source.
                argv.Add("-I");
                argv.Add("/sucrase.js");
                argv.Add("-e");
                argv.Add(VerifyTypeScriptBootstrap.Build(code, entry?.Name is { Length: > 0 } entryName ? entryName : "main.ts"));
                argv.Add("--");
                break;
            case VerifyRuntimeLanguage.Lua:
                // The entry file is Lua's script: every argument after it is the program's
                // (arg and ...), even one that starts with a dash. Its name was checked not to.
                argv.Add(entry?.Name ?? "");
                break;
            case VerifyRuntimeLanguage.C:
                // The program built from the sources: argv[0] is the entry's name without its extension.
                var builtFrom = entry?.Name ?? "main";
                var file = builtFrom[(builtFrom.LastIndexOf('/') + 1)..];
                argv[0] = file.Contains('.') ? file[..file.LastIndexOf('.')] : file;
                break;
            case VerifyRuntimeLanguage.Wasi:
                // The module is the program: argv[0] is its name, the rest the task's args.
                argv[0] = entry?.Name is { Length: > 0 } name ? name : runtime.Descriptor.Argv0;
                break;
        }

        argv.AddRange(args);
        return argv.ToArray();
    }

    /// <summary>The task's entry source: the one named by EntryPoint, else the first.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The entry source, or null for a task without sources.</returns>
    public static VerifySourceFileData? EntrySource(VerifyTaskData task)
    {
        return task.Sources.FirstOrDefault(s => s.Name == task.EntryPoint)
               ?? task.Sources.FirstOrDefault();
    }

    #endregion
}
