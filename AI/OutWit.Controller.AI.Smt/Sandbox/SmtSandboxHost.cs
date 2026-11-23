using OutWit.Controller.AI.Wasm;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Sandbox;

/// <summary>
/// The process-wide sandbox, made when it is first asked for and shared by every session, so the
/// solver module is compiled once per process - or the reason this process has none: another
/// wasmtime binding loaded first in the client's shared context, no module beside the
/// controller, a module with other bytes, a native library that is not the pinned one. The
/// reason is in fixed words and names no machine path.
/// <para>
/// A sandbox that could not be made is tried again at the next use: what kept it from starting
/// (a file that was still being unpacked) need not last as long as the process does.
/// </para>
/// </summary>
internal static class SmtSandboxHost
{
    #region Constants

    private const string SCOPE_LABEL = "smt";

    private static readonly object SYNC = new();

    #endregion

    #region Fields

    private static SmtSandbox? m_instance;

    private static IWitTempStorage? m_tempStorage;

    #endregion

    #region Functions

    /// <summary>
    /// Tells the sandbox where the host's own files of a run belong: the engine's temp storage,
    /// which on a contributor's machine is the client's folder. The first storage given before
    /// the sandbox exists is the one used; without one the operating system's temp folder is
    /// (tests, a host without the service).
    /// </summary>
    /// <param name="tempStorage">The host's temp storage, or null when the host has none.</param>
    public static void UseTempStorage(IWitTempStorage? tempStorage)
    {
        if (tempStorage != null)
            Interlocked.CompareExchange(ref m_tempStorage, tempStorage, null);
    }

    /// <summary>
    /// Gives the process-wide sandbox, making it at the first call: the module is compiled then,
    /// which takes tens of seconds, before any session's budget starts.
    /// </summary>
    /// <param name="problem">Why this process has no sandbox, in fixed words; null when it has one.</param>
    /// <returns>The sandbox, or null when this process cannot have one.</returns>
    public static SmtSandbox? Get(out string? problem)
    {
        problem = null;

        var instance = Volatile.Read(ref m_instance);
        if (instance != null)
            return instance;

        lock (SYNC)
        {
            if (m_instance != null)
                return m_instance;

            var storage = Volatile.Read(ref m_tempStorage);
            m_instance = Create(SmtModuleLocator.Locate(), storage?.CreateScope(SCOPE_LABEL), out problem);
            return m_instance;
        }
    }

    /// <summary>
    /// Makes a sandbox for a module file, or says in fixed words why there is none.
    /// </summary>
    /// <param name="modulePath">The module file, or null when none was found.</param>
    /// <param name="ioDirectory">The folder of the host's own files of a run, or null for one in the operating system's temp path.</param>
    /// <param name="problem">Why no sandbox was made; null when one was.</param>
    /// <returns>The sandbox, or null.</returns>
    public static SmtSandbox? Create(string? modulePath, string? ioDirectory, out string? problem)
    {
        problem = WasmHostBinding.Problem;
        if (problem != null)
            return null;

        if (modulePath == null)
        {
            problem = $"the solver module ({SmtModuleLocator.RelativePath}) is not in the controller's folder";
            return null;
        }

        try
        {
            return new SmtSandbox(modulePath, ioDirectory);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or Wasmtime.WasmtimeException)
        {
            // Our own words and the engine's: versions, sizes and hashes, no machine path.
            problem = $"the solver's sandbox cannot start: {exception.GetType().Name}: {exception.Message}";
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DllNotFoundException or BadImageFormatException or TypeInitializationException)
        {
            // These name a file of the machine: the kind of failure is enough.
            problem = $"the solver's sandbox cannot start: {exception.GetType().Name} reading the module or loading the engine library";
            return null;
        }
    }

    #endregion
}
