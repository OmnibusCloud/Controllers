using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OutWit.Controller.AI.Wasm;
using OutWit.Controller.Smt.Spike.Lang;
using OutWit.Controller.Smt.Spike.Protocol;
using OutWit.Controller.Smt.Spike.Wasm;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.Smt.Spike.Commands;

/// <summary>
/// The shared wasm host with the Z3 module compiled on its engine (once per process; optionally
/// from a serialized cache), and the two ways to run a problem: the interactive session
/// (<see cref="SmtWasmSession"/>) and the host's own <see cref="WasmHost.Run"/> with the whole
/// script as a stdin file (no change to the host at all).
/// </summary>
internal sealed class SpikeEngine : IDisposable
{
    #region Fields

    private readonly WasmHost m_host;
    private readonly WasmModule m_module;
    private readonly SmtWasmSession m_session;
    private readonly string m_captureDirectory;

    #endregion

    #region Constructors

    private SpikeEngine(WasmHost host, WasmModule module, string captureDirectory)
    {
        m_host = host;
        m_module = module;
        m_session = new SmtWasmSession(host, module);
        m_captureDirectory = captureDirectory;
    }

    #endregion

    #region Functions

    /// <summary>Makes the host and compiles (or loads) the module.</summary>
    /// <param name="modulePath">z3.wasm.</param>
    /// <param name="cachePath">A file for the serialized compiled module, or null.</param>
    /// <returns>The engine, with the compile measurements in <see cref="Meta"/>.</returns>
    public static SpikeEngine Create(string modulePath, string? cachePath)
    {
        var bytes = File.ReadAllBytes(modulePath);
        var clock = Stopwatch.StartNew();
        var host = new WasmHost();
        var hostMs = clock.ElapsedMilliseconds;

        clock.Restart();
        WasmModule module;
        long deserializeMs = -1;
        long compileMs = -1;
        if (cachePath != null && File.Exists(cachePath))
        {
            module = WasmModule.Deserialize(host.Engine, "z3", File.ReadAllBytes(cachePath));
            deserializeMs = clock.ElapsedMilliseconds;
        }
        else
        {
            module = WasmModule.FromBytes(host.Engine, "z3", bytes);
            compileMs = clock.ElapsedMilliseconds;
            if (cachePath != null)
            {
                File.WriteAllBytes(cachePath, module.Serialize());
                clock.Restart();
                using var reloaded = WasmModule.Deserialize(host.Engine, "z3", File.ReadAllBytes(cachePath));
                deserializeMs = clock.ElapsedMilliseconds;
            }
        }

        var capture = Path.Combine(Path.GetTempPath(), "smt-sm0-capture-" + Environment.ProcessId);
        var engine = new SpikeEngine(host, module, capture);
        var native = WasmHostNativeLibrary.ResolvedPath ?? WasmHostNativeLibrary.Locate();
        var nativeSha = native != null && File.Exists(native) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native))).ToLowerInvariant() : null;
        engine.Meta = new Dictionary<string, object?>
        {
            ["kind"] = "meta",
            ["platform"] = RuntimeInformation.RuntimeIdentifier,
            ["os"] = RuntimeInformation.OSDescription,
            ["arch"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["cpus"] = Environment.ProcessorCount,
            ["module_sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            ["module_bytes"] = bytes.Length,
            ["host_ms"] = hostMs,
            ["compile_ms"] = compileMs,
            ["deserialize_ms"] = deserializeMs,
            ["explicit_traps"] = host.ExplicitTraps,
            ["wasmtime_native"] = native,
            ["wasmtime_native_sha256"] = nativeSha,
            ["binding"] = WasmHostBinding.Loaded?.ToString()
        };
        return engine;
    }

    /// <summary>Runs one interactive session.</summary>
    /// <param name="problem">The canonical problem.</param>
    /// <param name="options">The envelope.</param>
    /// <param name="projection">For enumerate: the projection; null for check.</param>
    /// <param name="maxModels">For enumerate: K.</param>
    /// <returns>The driver (the answers) and the outcome (how it ended, what it cost).</returns>
    public (SmtSessionDriver Driver, SmtSessionOutcome Outcome) Session(SmtProblem problem, SpikeOptions options, IReadOnlyList<SExpr>? projection = null, int maxModels = 2)
    {
        var driver = new SmtSessionDriver(problem, SpikeProblems.Nonce(problem), options.Rlimit, Stopwatch.StartNew(), projection, maxModels);
        var outcome = m_session.Run(options.Argv(), driver, options.Fuel, options.WasmMemoryBytes, options.WallMs, options.Seed, options.StdoutLimit);
        return (driver, outcome);
    }

    /// <summary>
    /// Runs one problem through the shared host unchanged: the whole script (every query of a
    /// check, sat or not) as the guest's stdin file, the answers read from its stdout afterwards.
    /// The fuel includes instantiation (the host's budget is given before it).
    /// </summary>
    /// <param name="problem">The canonical problem.</param>
    /// <param name="options">The envelope.</param>
    /// <returns>The host's outcome and the stdout's answer blocks.</returns>
    public (WasmHostRunOutcome Outcome, string Script) Batch(SmtProblem problem, SpikeOptions options)
    {
        var (script, _) = SpikeScripts.Check(problem, options.Rlimit);
        var outcome = m_host.Run(m_module, new WasmHostRunRequest
        {
            Argv = options.Argv(),
            Stdin = script,
            CaptureDirectory = m_captureDirectory,
            Fuel = options.Fuel,
            MemoryBytes = options.WasmMemoryBytes,
            WallMs = options.WallMs,
            RandomSeed = options.Seed,
            StdoutLimitBytes = options.StdoutLimit,
            StderrLimitBytes = 64 << 10,
            OutOfMemory = stderr => stderr.Contains("out of memory", StringComparison.Ordinal)
        });
        return (outcome, script);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        m_module.Dispose();
        m_host.Dispose();
        try
        {
            if (Directory.Exists(m_captureDirectory))
                Directory.Delete(m_captureDirectory, true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    #endregion

    #region Properties

    /// <summary>What was measured making the engine, and where it runs.</summary>
    public Dictionary<string, object?> Meta { get; private set; } = [];

    #endregion
}
