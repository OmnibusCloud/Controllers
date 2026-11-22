using System.Runtime.CompilerServices;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>
/// The process-wide PDF sandbox, made on first use and shared by every shard, so the module is
/// compiled once per process — or the reason this process cannot have one: the binding guard
/// (another wasmtime binding loaded first in the client's shared context), no module beside the
/// controller, a module with other bytes, a native library that does not load. Without a
/// sandbox, a shard that holds a PDF fails on this node as a fault of the node and runs elsewhere
/// (<see cref="PdfSandboxException"/>); a shard without one runs here as anywhere. The reason names
/// no machine path.
/// </summary>
public static class PdfSandboxHost
{
    #region Fields

    private static readonly Lazy<(PdfSandbox? Sandbox, string? Problem)> INSTANCE = new(Create, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string?> DIAGNOSIS = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    private static volatile StrongBox<(PdfSandbox? Sandbox, string? Problem)>? m_override;

    #endregion

    #region Functions

    /// <summary>
    /// What this process can say about the stage without keeping a sandbox it does not use: the
    /// shared one's state once it exists, else a sandbox made and released at once (once per
    /// process; the answer is kept).
    /// </summary>
    /// <returns>null when the stage can run here, else the reason.</returns>
    public static string? Diagnose()
    {
        return INSTANCE.IsValueCreated ? INSTANCE.Value.Problem : DIAGNOSIS.Value;
    }

    /// <summary>Makes the process answer as if it had this sandbox or none (tests of a node without one); <see cref="ClearOverride"/> undoes it.</summary>
    /// <param name="sandbox">The sandbox, or null for none.</param>
    /// <param name="problem">Why there is none.</param>
    internal static void Override(PdfSandbox? sandbox, string? problem)
    {
        m_override = new StrongBox<(PdfSandbox?, string?)>((sandbox, sandbox == null ? problem ?? "no PDF sandbox" : null));
    }

    /// <summary>Undoes <see cref="Override"/>.</summary>
    internal static void ClearOverride()
    {
        m_override = null;
    }

    private static string? Probe()
    {
        var (sandbox, problem) = Create();
        sandbox?.Dispose();
        return problem;
    }

    private static (PdfSandbox? Sandbox, string? Problem) Create()
    {
        if (WasmHostBinding.Problem is { } binding)
            return (null, binding);

        var path = PdfModuleLocator.Locate();
        if (path == null)
            return (null, $"the PDF extractor module ({PdfModuleLocator.RelativePath}) is not in the controller's folder");

        try
        {
            return (new PdfSandbox(path), null);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or Wasmtime.WasmtimeException)
        {
            // Our own words and the engine's: versions and hashes, no machine path.
            return (null, $"the PDF sandbox cannot start: {exception.GetType().Name}: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DllNotFoundException or BadImageFormatException or TypeInitializationException)
        {
            // These name a file of the machine: the kind of failure is enough.
            return (null, $"the PDF sandbox cannot start: {exception.GetType().Name} reading the module or loading the engine library");
        }
    }

    #endregion

    #region Properties

    /// <summary>The process-wide sandbox, or null when this process cannot have one (see <see cref="Problem"/>).</summary>
    public static PdfSandbox? Instance => m_override is { } forced ? forced.Value.Sandbox : INSTANCE.Value.Sandbox;

    /// <summary>Why this process has no sandbox; null when it has one.</summary>
    public static string? Problem => m_override is { } forced ? forced.Value.Problem : INSTANCE.Value.Problem;

    #endregion
}
