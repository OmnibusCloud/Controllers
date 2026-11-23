using System.Diagnostics;
using System.Security.Cryptography;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Wasm;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Smt.Sandbox;

/// <summary>
/// The solver in the controller's own copy of the wasm sandbox host: one engine with WebAssembly
/// exceptions on (the solver is a C++ program that throws; the module is the controller's own,
/// pinned by hash, and no other module is ever compiled on this engine), and the solver module
/// compiled on it once. A session is one run of the module in a fresh store: no folder of the
/// machine is visible to it, its stdin is a conversation with the caller, its stdout and stderr
/// are the host's own bounded streams, its fuel is given after instantiation.
/// <para>
/// The module is held to the profile's size and SHA-256 before a byte of it is compiled. The
/// compile takes tens of seconds: it happens here, on construction, outside the budget of any
/// session, and <see cref="CompileMs"/> keeps how long it took. One sandbox serves every session
/// of a process, concurrently.
/// </para>
/// </summary>
internal sealed class SmtSandbox : IDisposable
{
    #region Constants

    private const string MODULE_NAME = "z3";
    private const string IO_FOLDER_PREFIX = "witai_smt_";

    private static readonly TimeSpan CLEANUP_WAIT = TimeSpan.FromSeconds(5);

    #endregion

    #region Fields

    private readonly WasmHost m_host;
    private readonly WasmModule m_module;
    private readonly string m_ioDirectory;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a sandbox for the module at a path: checks the bytes, makes the engine, compiles.
    /// </summary>
    /// <param name="modulePath">The module file.</param>
    /// <param name="ioDirectory">
    /// The folder the host keeps a run's two empty stdio files in while it runs, or null for a new
    /// one in the operating system's temp path. On a node it is a scope of the client's temp
    /// storage. Created when missing, deleted on Dispose.
    /// </param>
    /// <param name="explicitTraps">
    /// True to make every trap an explicit check whatever the process, as a host with hardware
    /// stack protection has them by itself; false leaves the choice to the process. An outcome is
    /// the same either way, fuel included.
    /// </param>
    /// <exception cref="InvalidDataException">The file's bytes are not the pinned module.</exception>
    /// <exception cref="InvalidOperationException">The loaded wasmtime binding is not the one this controller was built against, the native library is not the pinned one or none was found, or the engine cannot be made.</exception>
    public SmtSandbox(string modulePath, string? ioDirectory = null, bool explicitTraps = false)
    {
        var bytes = File.ReadAllBytes(modulePath);
        if (bytes.LongLength != SmtProfile.MODULE_SIZE)
            throw new InvalidDataException($"the solver module has {bytes.LongLength} bytes, not the pinned {SmtProfile.MODULE_SIZE}");

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(sha256, SmtProfile.MODULE_SHA256, StringComparison.Ordinal))
            throw new InvalidDataException($"the solver module hashes to {sha256}, not the pinned {SmtProfile.MODULE_SHA256}");

        m_host = new WasmHost(parallelCompilation: true, explicitTraps, exceptions: true);
        try
        {
            var clock = Stopwatch.StartNew();
            m_module = WasmModule.FromBytes(m_host.Engine, MODULE_NAME, bytes);
            CompileMs = clock.ElapsedMilliseconds;

            m_ioDirectory = ioDirectory ?? Path.Combine(Path.GetTempPath(), $"{IO_FOLDER_PREFIX}{Guid.NewGuid():N}");
            Directory.CreateDirectory(m_ioDirectory);
        }
        catch
        {
            m_module?.Dispose();
            m_host.Dispose();
            throw;
        }
    }

    #endregion

    #region Functions

    /// <summary>
    /// Runs one session of the solver to its end: a fresh instance in the envelope, its stdin
    /// served by the conversation. Never throws for anything the solver did: every way it can
    /// end is in the outcome.
    /// </summary>
    /// <param name="conversation">The caller's side: what the solver wrote since the last ask in, its next input out, empty for the end of input.</param>
    /// <param name="envelope">The session's envelope.</param>
    /// <returns>How the run ended and what it cost.</returns>
    /// <exception cref="IOException">The host's own files could not be used, or the conversation threw: a fault of the node, not the solver's.</exception>
    /// <exception cref="ObjectDisposedException">The sandbox is disposed.</exception>
    public WasmHostRunOutcome Run(WasmHostConversation conversation, SmtEnvelope envelope)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        return m_host.Run(m_module, new WasmHostRunRequest
        {
            Argv = SmtProfile.Arguments(envelope),
            Conversation = conversation,
            CaptureDirectory = m_ioDirectory,
            Fuel = envelope.Fuel,
            FuelAfterInstantiation = true,
            MemoryBytes = envelope.HostMemoryBytes,
            WallMs = envelope.BackstopMs,
            DiscardInBackground = true,
            StdoutLimitBytes = envelope.OutputLimitBytes,
            StopAtStdoutLimit = true,
            StderrLimitBytes = SmtProfile.ERROR_OUTPUT_LIMIT_BYTES,
            OutOfMemory = stderr => stderr.Contains(SmtProfile.OUT_OF_MEMORY_MARKER, StringComparison.Ordinal)
        });
    }

    #endregion

    #region IDisposable

    /// <summary>Releases the compiled module, then the host (its engine and epoch timer), and removes the folder of the stdio files.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_module.Dispose();
        m_host.Dispose();

        // The stdio files of the runs are deleted in the background: wait for that before the folder goes.
        WasmHostDiscard.Drain(CLEANUP_WAIT);
        try
        {
            if (Directory.Exists(m_ioDirectory))
                Directory.Delete(m_ioDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Two empty files a run; a folder that is still held is left to the temp storage's owner.
        }
    }

    #endregion

    #region Properties

    /// <summary>How long compiling the module took in this process, milliseconds: a fact about the machine, for diagnostics.</summary>
    public long CompileMs { get; }

    /// <summary>Whether the solver's traps are explicit checks in this sandbox.</summary>
    public bool ExplicitTraps => m_host.ExplicitTraps;

    /// <summary>What the compiled module is valid for: the native library, the trap mode and the exceptions proposal.</summary>
    public string CompiledCodeKey => m_host.CompiledCodeKey;

    #endregion
}
