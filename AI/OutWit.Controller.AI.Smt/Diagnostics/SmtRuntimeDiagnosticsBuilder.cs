using System.Security.Cryptography;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Smt.Diagnostics;

/// <summary>
/// Builds what the Smt controller of this process carries and can do
/// (<see cref="SmtRuntimeDiagnosticsData"/>). Asking compiles nothing: a process that has not
/// solved a problem yet says that it has the module and has not compiled it. Nothing in it names
/// the machine.
/// </summary>
internal static class SmtRuntimeDiagnosticsBuilder
{
    #region Constants

    /// <summary>The trap mode of a sandbox whose traps are explicit checks in the compiled code.</summary>
    public const string TRAPS_EXPLICIT = "explicit";

    /// <summary>The trap mode of a sandbox whose traps are the hardware's, taken by signal handlers.</summary>
    public const string TRAPS_SIGNALS = "signals";

    #endregion

    #region Functions

    /// <summary>Builds the diagnostics of this process.</summary>
    /// <returns>The diagnostics.</returns>
    public static SmtRuntimeDiagnosticsData Build()
    {
        var sandbox = SmtSandboxHost.Current;
        var degree = SmtSandboxDegree.ForCurrentMachine(SmtSandboxDegree.SessionBytes(new SmtOptionsData()));
        var platform = WasmHostNativeLibrary.PortableRid;
        var explicitTraps = sandbox?.ExplicitTraps ?? WasmHostEngineNative.HostEnforcesShadowStacks();

        return new SmtRuntimeDiagnosticsData
        {
            ControllerVersion = ControllerBuildInfo.VERSION,
            ProfileId = SmtProfile.ID,
            SolverVersion = SmtProfile.SOLVER_VERSION,
            ModuleFile = SmtModuleLocator.RelativePath,
            ModuleSha256 = SmtProfile.MODULE_SHA256,
            ModuleSize = SmtProfile.MODULE_SIZE,
            ModulePresent = sandbox != null || IsPinnedModule(SmtModuleLocator.Locate()),
            ModuleCompiled = sandbox != null,
            CompileMs = sandbox?.CompileMs ?? 0,
            SandboxProblem = sandbox == null ? WasmHostBinding.Problem ?? SmtSandboxHost.LastProblem ?? "" : "",
            WasmtimeBinding = WasmHostBinding.Loaded?.ToString() ?? "",
            NativesVersion = WasmHostNatives.VERSION,
            NativesSha256 = WasmHostNatives.PINNED.GetValueOrDefault(platform, ""),
            NativesPinned = WasmHostBinding.Problem == null && WasmHostNatives.LoadedProblem() == null,
            TrapMode = explicitTraps ? TRAPS_EXPLICIT : TRAPS_SIGNALS,
            Platform = platform,
            LogicalCores = degree.Cores,
            AvailableMemoryBytes = degree.AvailableBytes,
            SessionMemoryBytes = degree.SessionBytes,
            Degree = degree.Degree,
            DegreeReason = degree.Reason
        };
    }

    /// <summary>Whether a file is the pinned solver module: its size and its SHA-256.</summary>
    /// <param name="path">The file, or null.</param>
    /// <returns>True when it is.</returns>
    public static bool IsPinnedModule(string? path)
    {
        try
        {
            if (path == null || !File.Exists(path) || new FileInfo(path).Length != SmtProfile.MODULE_SIZE)
                return false;

            using var stream = File.OpenRead(path);
            return string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), SmtProfile.MODULE_SHA256, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    #endregion
}
