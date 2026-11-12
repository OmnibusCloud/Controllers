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
    Ruby = 3
}
