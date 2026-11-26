using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// What the Smt controller of the process that answers carries and can do: its version and its
/// profile, the solver module's pin and whether this process has the module and has compiled it,
/// the engine library's pin and whether it is the one loaded, the trap mode, and how many
/// sessions it would run at once, with why. A plain script function runs on the host, so through
/// a diagnostics script this describes the host's module.
/// <para>
/// Nothing here names the machine: the platform is a runtime identifier, the cores and the
/// memory are numbers, and a reason is in fixed words without a path.
/// </para>
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order. A published layout
// changes only behind a version fence, so that no reader of the old layout is ever handed the
// new one: MemoryPack throws on a payload of more members than its reader knows, and a member a
// sender may not have arrives as its type's default (null for a text or a list), never as its
// initializer; such a member starts at that default, and a reference one is declared nullable.
public sealed partial class SmtRuntimeDiagnosticsData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SmtRuntimeDiagnosticsData other)
            return false;

        return ControllerVersion.Is(other.ControllerVersion)
               && ProfileId.Is(other.ProfileId)
               && SolverVersion.Is(other.SolverVersion)
               && ModuleFile.Is(other.ModuleFile)
               && ModuleSha256.Is(other.ModuleSha256)
               && ModuleSize.Is(other.ModuleSize)
               && ModulePresent.Is(other.ModulePresent)
               && ModuleCompiled.Is(other.ModuleCompiled)
               && CompileMs.Is(other.CompileMs)
               && SandboxProblem.Is(other.SandboxProblem)
               && WasmtimeBinding.Is(other.WasmtimeBinding)
               && NativesVersion.Is(other.NativesVersion)
               && NativesSha256.Is(other.NativesSha256)
               && NativesPinned.Is(other.NativesPinned)
               && TrapMode.Is(other.TrapMode)
               && Platform.Is(other.Platform)
               && LogicalCores.Is(other.LogicalCores)
               && AvailableMemoryBytes.Is(other.AvailableMemoryBytes)
               && SessionMemoryBytes.Is(other.SessionMemoryBytes)
               && Degree.Is(other.Degree)
               && DegreeReason.Is(other.DegreeReason);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override SmtRuntimeDiagnosticsData Clone()
    {
        return new SmtRuntimeDiagnosticsData
        {
            ControllerVersion = ControllerVersion,
            ProfileId = ProfileId,
            SolverVersion = SolverVersion,
            ModuleFile = ModuleFile,
            ModuleSha256 = ModuleSha256,
            ModuleSize = ModuleSize,
            ModulePresent = ModulePresent,
            ModuleCompiled = ModuleCompiled,
            CompileMs = CompileMs,
            SandboxProblem = SandboxProblem,
            WasmtimeBinding = WasmtimeBinding,
            NativesVersion = NativesVersion,
            NativesSha256 = NativesSha256,
            NativesPinned = NativesPinned,
            TrapMode = TrapMode,
            Platform = Platform,
            LogicalCores = LogicalCores,
            AvailableMemoryBytes = AvailableMemoryBytes,
            SessionMemoryBytes = SessionMemoryBytes,
            Degree = Degree,
            DegreeReason = DegreeReason
        };
    }

    #endregion

    #region Properties

    /// <summary>The controller's version.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public string ControllerVersion { get; set; } = "";

    /// <summary>The id of the controller's one solver profile, as a run's options name it.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public string ProfileId { get; set; } = "";

    /// <summary>The solver's version.</summary>
    [MemoryPackOrder(2)]
    public string SolverVersion { get; set; } = "";

    /// <summary>Where the solver module is looked for, relative to the controller's folder.</summary>
    [MemoryPackOrder(3)]
    public string ModuleFile { get; set; } = "";

    /// <summary>The solver module's pinned SHA-256, lower-case hex.</summary>
    [MemoryPackOrder(4)]
    public string ModuleSha256 { get; set; } = "";

    /// <summary>The solver module's pinned size, bytes.</summary>
    [MemoryPackOrder(5)]
    public long ModuleSize { get; set; }

    /// <summary>Whether this process has the solver module, with the pinned size and SHA-256.</summary>
    [ToString]
    [MemoryPackOrder(6)]
    public bool ModulePresent { get; set; }

    /// <summary>Whether this process has compiled the module (its sandbox exists).</summary>
    [ToString]
    [MemoryPackOrder(7)]
    public bool ModuleCompiled { get; set; }

    /// <summary>How long the compile took in this process, milliseconds; 0 when it has not compiled the module.</summary>
    [MemoryPackOrder(8)]
    public long CompileMs { get; set; }

    /// <summary>Why this process has no sandbox, in fixed words without a path; empty when it has one or has not tried to make one.</summary>
    [MemoryPackOrder(9)]
    public string SandboxProblem { get; set; } = "";

    /// <summary>The version of the wasmtime .NET binding loaded in this process; empty when none could be loaded.</summary>
    [MemoryPackOrder(10)]
    public string WasmtimeBinding { get; set; } = "";

    /// <summary>The version of the native engine library the controller ships.</summary>
    [MemoryPackOrder(11)]
    public string NativesVersion { get; set; } = "";

    /// <summary>The SHA-256 of the native engine library the controller ships for this platform; empty when it ships none for it.</summary>
    [MemoryPackOrder(12)]
    public string NativesSha256 { get; set; } = "";

    /// <summary>Whether the native engine library of this process is the pinned one.</summary>
    [ToString]
    [MemoryPackOrder(13)]
    public bool NativesPinned { get; set; }

    /// <summary>How the solver's traps are taken: <c>explicit</c> (checks in the compiled code, as a host with hardware stack protection needs) or <c>signals</c>.</summary>
    [ToString]
    [MemoryPackOrder(14)]
    public string TrapMode { get; set; } = "";

    /// <summary>The platform: <c>win-x64</c>, <c>linux-x64</c>, <c>osx-arm64</c>.</summary>
    [ToString]
    [MemoryPackOrder(15)]
    public string Platform { get; set; } = "";

    /// <summary>The logical cores of the machine.</summary>
    [MemoryPackOrder(16)]
    public int LogicalCores { get; set; }

    /// <summary>The memory the machine has available as the runtime sees it, bytes; 0 when it cannot say.</summary>
    [MemoryPackOrder(17)]
    public long AvailableMemoryBytes { get; set; }

    /// <summary>The memory one session may take under the default options, bytes.</summary>
    [MemoryPackOrder(18)]
    public long SessionMemoryBytes { get; set; }

    /// <summary>How many sessions a package under the default options would run at once here; 0 when not one fits.</summary>
    [ToString]
    [MemoryPackOrder(19)]
    public int Degree { get; set; }

    /// <summary>What decided <see cref="Degree"/>: <c>cores</c>, <c>cap</c>, <c>memory</c>, or <c>no_memory</c> when not one session fits.</summary>
    [ToString]
    [MemoryPackOrder(20)]
    public string DegreeReason { get; set; } = "";

    #endregion
}
