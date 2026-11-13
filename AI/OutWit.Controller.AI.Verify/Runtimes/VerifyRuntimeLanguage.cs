namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// Language family of a runtime module — drives the sandbox invocation shape
/// (argv layout, stdlib preopen, environment).
/// </summary>
public enum VerifyRuntimeLanguage
{
    Python = 0,
    JavaScript = 1,

    /// <summary>
    /// No interpreter: the task's entry source is itself a WASI command module (its bytes,
    /// base64), run as compiled — Rust, C/C++, Zig, TinyGo and anything else that targets
    /// wasm32-wasi. argv[0] is the entry name; args, stdin, suite and limits as for any task.
    /// </summary>
    Wasi = 2,

    /// <summary>
    /// Ruby (CRuby for WASI): the entry source runs inline (-e) without RubyGems; the
    /// standard library is a pinned tree of files, the guest's whole read-only file system.
    /// </summary>
    Ruby = 3,

    /// <summary>
    /// Lua (the stock interpreter, built for WASI by the controller's toolchain): the task's
    /// sources are files in a read-only folder that is the guest's whole file system, and
    /// the entry file runs as Lua's script, so arg, ... and require of a neighbour work.
    /// </summary>
    Lua = 4,

    /// <summary>
    /// TypeScript on the JavaScript runtime's QuickJS: sucrase (loaded first from the read-only
    /// root) strips the types of the inline source, and the result runs as a JavaScript module.
    /// No type check; a runtime namespace is not supported.
    /// </summary>
    TypeScript = 5
}
