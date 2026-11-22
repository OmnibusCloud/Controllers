using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Process-wide sandbox, shared by every Verify node activity so a runtime module is
/// compiled once per process (the expensive step) and reused across batches — the same
/// singleton-adapter caching discipline the other controllers use for heavy state.
/// A sandbox that could not be made is tried again at the next use: what kept it from starting
/// (a library that was still being unpacked) need not last as long as the process does.
/// </summary>
public static class VerifySandboxHost
{
    #region Constants

    private const string SCOPE_LABEL = "verify";

    #endregion

    #region Fields

    private static readonly object s_lock = new();

    private static VerifyWasmSandbox? s_instance;

    private static IWitTempStorage? s_tempStorage;

    #endregion

    #region Functions

    /// <summary>
    /// Tells the sandbox where its working files belong: the host's own temp storage. On a
    /// contributor's machine that is the client's folder, and nothing of a task (the files a
    /// run's input and output are redirected to) is written anywhere else. The first storage
    /// given before the sandbox exists is the one used; a host without the service leaves the
    /// operating system's temp folder as the fallback (tests, a doctor run).
    /// </summary>
    /// <param name="tempStorage">The host's temp storage, or null when the host has none.</param>
    public static void UseTempStorage(IWitTempStorage? tempStorage)
    {
        if (tempStorage != null)
            Interlocked.CompareExchange(ref s_tempStorage, tempStorage, null);
    }

    private static VerifyWasmSandbox Create()
    {
        var storage = Volatile.Read(ref s_tempStorage);

        return storage == null
            ? new VerifyWasmSandbox()
            : new VerifyWasmSandbox(storage.CreateScope(SCOPE_LABEL));
    }

    #endregion

    #region Properties

    /// <summary>The process-wide sandbox, created on first use and, when that failed, at the next one.</summary>
    /// <exception cref="InvalidOperationException">The sandbox cannot start: the message says why, in fixed words.</exception>
    public static VerifyWasmSandbox Instance
    {
        get
        {
            var instance = Volatile.Read(ref s_instance);
            if (instance != null)
                return instance;

            lock (s_lock)
            {
                return s_instance ??= Create();
            }
        }
    }

    #endregion
}
