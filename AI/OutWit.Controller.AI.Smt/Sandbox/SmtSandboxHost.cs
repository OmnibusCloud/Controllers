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

    private static string? m_lastProblem;

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

            m_instance = Make(SmtModuleLocator.Locate(), Volatile.Read(ref m_tempStorage), out problem);
            Volatile.Write(ref m_lastProblem, problem);
            return m_instance;
        }
    }

    /// <summary>
    /// Makes a sandbox whose own files of a run live in a scope of a temp storage, or says in fixed
    /// words why there is none. A process that has no sandbox asks again at every package and at
    /// every benchmark, so an attempt that fails leaves nothing behind: the scope is made only
    /// when what can be known without it allows a sandbox (the binding, a module file), and it is
    /// given back when no sandbox comes of it.
    /// </summary>
    /// <param name="modulePath">The module file, or null when none was found.</param>
    /// <param name="storage">The host's temp storage, or null for a folder in the operating system's temp path.</param>
    /// <param name="problem">Why no sandbox was made; null when one was.</param>
    /// <returns>The sandbox, or null.</returns>
    public static SmtSandbox? Make(string? modulePath, IWitTempStorage? storage, out string? problem)
    {
        problem = Unavailable(modulePath);
        if (problem != null)
            return null;

        var scope = storage?.CreateScope(SCOPE_LABEL);
        var sandbox = Create(modulePath, scope, out problem);
        if (sandbox == null && scope != null)
            GiveBack(storage!, scope);

        return sandbox;
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
        problem = Unavailable(modulePath);
        if (problem != null)
            return null;

        try
        {
            return new SmtSandbox(modulePath!, ioDirectory);
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

    #region Tools

    /// <summary>What is known against a sandbox before anything is made for it: another binding in the process, no module file.</summary>
    private static string? Unavailable(string? modulePath)
    {
        return WasmHostBinding.Problem
               ?? (modulePath == null ? $"the solver module ({SmtModuleLocator.RelativePath}) is not in the controller's folder" : null);
    }

    /// <summary>Hands a scope back to the storage that made it; a storage that cannot take it back keeps it for its owner's own sweep.</summary>
    private static void GiveBack(IWitTempStorage storage, string scope)
    {
        try
        {
            storage.DeleteScope(scope);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // an empty folder inside the host's own storage
        }
    }

    #endregion

    #region Properties

    /// <summary>The process-wide sandbox when it has been made, else null; asking never makes it.</summary>
    public static SmtSandbox? Current => Volatile.Read(ref m_instance);

    /// <summary>Why the last attempt to make the sandbox failed, in fixed words; null when it has not failed or has not been made yet.</summary>
    public static string? LastProblem => Volatile.Read(ref m_lastProblem);

    #endregion
}
