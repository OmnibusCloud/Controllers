using Wasmtime;
using WasmEngine = Wasmtime.Engine;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Verify.Assets;

/// <summary>
/// Runs the pinned CPython-WASI interpreter on the build machine, with the pristine runtime
/// tree read-only as "/" and a work tree read-write as "/src" — the way the bytecode layers
/// are compiled by the very interpreter that will load them.
/// </summary>
internal static class WasiPython
{
    #region Constants

    private const string GUEST_ROOT = "/";
    public const string GUEST_SOURCE = "/src";
    private const long MEMORY_LIMIT = 2L * 1024 * 1024 * 1024;

    #endregion

    #region Functions

    /// <summary>
    /// Runs python.wasm once.
    /// </summary>
    /// <param name="pythonWasm">The interpreter module.</param>
    /// <param name="rootDir">The host folder preopened read-only as "/" (holds lib/python3.14).</param>
    /// <param name="sourceDir">The host folder preopened read-write as "/src", or null.</param>
    /// <param name="args">Interpreter arguments (after argv[0]).</param>
    /// <returns>Exit code, stdout, stderr.</returns>
    public static WasiRunResult Run(string pythonWasm, string rootDir, string? sourceDir, IReadOnlyList<string> args)
    {
        using var engine = new WasmEngine(new Config().WithCraneliftNaNCanonicalization(true));
        using var module = WasmModule.FromFile(engine, pythonWasm);

        var ioDir = Path.Combine(Path.GetTempPath(), $"verify-assets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(ioDir);
        var stdoutPath = Path.Combine(ioDir, "stdout");
        var stderrPath = Path.Combine(ioDir, "stderr");

        try
        {
            var wasi = new WasiConfiguration()
                .WithArgs(["python.wasm", .. args])
                .WithEnvironmentVariable("PYTHONHASHSEED", "0")
                .WithEnvironmentVariable("PYTHONUNBUFFERED", "1")
                .WithPreopenedDirectory(rootDir, GUEST_ROOT, false);

            if (sourceDir != null)
            {
                wasi = wasi.WithPreopenedDirectory(sourceDir, GUEST_SOURCE,
                    true);
            }

            wasi = wasi.WithStandardOutput(stdoutPath).WithStandardError(stderrPath);

            var exitCode = 0;
            var store = new Store(engine);
            try
            {
                store.SetWasiConfiguration(wasi);
                store.SetLimits(memorySize: MEMORY_LIMIT);

                using var linker = new Linker(engine);
                linker.DefineWasi();

                try
                {
                    var instance = linker.Instantiate(store, module);
                    var start = instance.GetAction("_start") ?? throw new InvalidOperationException("python.wasm exports no _start");
                    start();
                }
                catch (WasmtimeException exception) when (exception.ExitCode is { } code)
                {
                    exitCode = code;
                }
            }
            finally
            {
                store.Dispose();
            }

            return new WasiRunResult(exitCode, ReadOrEmpty(stdoutPath), ReadOrEmpty(stderrPath));
        }
        finally
        {
            try
            {
                Directory.Delete(ioDir, recursive: true);
            }
            catch
            {
                // temp; best-effort
            }
        }
    }

    private static string ReadOrEmpty(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    #endregion
}

/// <summary>The outcome of one interpreter run.</summary>
internal sealed record WasiRunResult(int ExitCode, string Stdout, string Stderr);
