using System.Security.Cryptography;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Smt.Live.Local;

/// <summary>
/// The solver's sandbox of a local run, made by the controller's own factory
/// (<see cref="SmtSandboxHost.Create"/>, the one a node's process-wide sandbox is made by), from
/// the module the controller's own locator finds (<see cref="SmtModuleLocator"/>: the variable
/// <c>WITAI_SMT_Z3</c>, else <c>wasm/z3-5.4.1.wasm</c> beside the controller's assembly). The
/// controller holds the module to the size and the SHA-256 its profile pins before it compiles a
/// byte of it, and the native engine library to the SHA-256 its natives guard pins; a module or a
/// library of other bytes gives no sandbox, with the controller's reason. What was loaded is kept
/// here, hashed again from the files, for the run's description.
/// </summary>
internal sealed class SmtLiveSandbox : IDisposable
{
    #region Fields

    private bool m_disposed;

    #endregion

    #region Constructors

    private SmtLiveSandbox(SmtSandbox sandbox, string modulePath, string? nativesPath, TimeSpan compile)
    {
        Sandbox = sandbox;
        ModulePath = modulePath;
        ModuleSha256 = Sha256Of(modulePath);
        NativesPath = nativesPath;
        NativesSha256 = nativesPath == null ? null : Sha256Of(nativesPath);
        Compile = compile;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Makes the sandbox: finds the module, holds it and the engine library to their pins and
    /// compiles the module (tens of seconds).
    /// </summary>
    /// <param name="ioFolder">The folder the sandbox keeps its sessions' stdio files in; deleted with the sandbox.</param>
    /// <param name="problem">Why there is no sandbox, in the controller's words; null when there is one.</param>
    /// <returns>The sandbox, or null.</returns>
    public static SmtLiveSandbox? Open(string ioFolder, out string? problem)
    {
        var module = SmtModuleLocator.Locate();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var sandbox = SmtSandboxHost.Create(module, ioFolder, out problem);
        if (sandbox == null)
            return null;

        return new SmtLiveSandbox(sandbox, module!, WasmHostNativeLibrary.LoadedPath, clock.Elapsed);
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    #endregion

    #region IDisposable

    /// <summary>Releases the compiled module and the engine, and removes the stdio folder.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        Sandbox.Dispose();
    }

    #endregion

    #region Properties

    /// <summary>The controller's sandbox.</summary>
    public SmtSandbox Sandbox { get; }

    /// <summary>The module file the controller compiled.</summary>
    public string ModulePath { get; }

    /// <summary>The SHA-256 of the module file, lower-case hex.</summary>
    public string ModuleSha256 { get; }

    /// <summary>Whether the module is the one the profile pins (it is, or there would be no sandbox).</summary>
    public bool ModulePinned => string.Equals(ModuleSha256, SmtProfile.MODULE_SHA256, StringComparison.Ordinal);

    /// <summary>The native engine library the binding loaded, or null when the process cannot tell.</summary>
    public string? NativesPath { get; }

    /// <summary>The SHA-256 of the native engine library, lower-case hex, or null.</summary>
    public string? NativesSha256 { get; }

    /// <summary>Whether the native library is the one pinned for this platform (it is, or there would be no sandbox).</summary>
    public bool NativesPinned => NativesSha256 != null
                                 && WasmHostNatives.PINNED.TryGetValue(WasmHostNativeLibrary.PortableRid, out var pinned)
                                 && string.Equals(NativesSha256, pinned, StringComparison.Ordinal);

    /// <summary>How long making the sandbox took: reading and checking the module, starting the engine, compiling.</summary>
    public TimeSpan Compile { get; }

    #endregion
}
