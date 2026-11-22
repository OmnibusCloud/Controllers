using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// A node's sandbox runtime inventory: the wasmtime host version and the pinned
/// language runtimes it can serve. Mirrors Render.RuntimeDiagnostics.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifyRuntimeDiagnosticsData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifyRuntimeDiagnosticsData other)
            return false;

        if (Runtimes.Count != other.Runtimes.Count)
            return false;

        for (var i = 0; i < Runtimes.Count; i++)
        {
            if (!Runtimes[i].Is(other.Runtimes[i], tolerance))
                return false;
        }

        return RuntimeTarget.Is(other.RuntimeTarget)
               && WasmtimeVersion.Is(other.WasmtimeVersion)
               && RuntimesRootFound.Is(other.RuntimesRootFound);
    }

    public override VerifyRuntimeDiagnosticsData Clone()
    {
        return new VerifyRuntimeDiagnosticsData
        {
            RuntimeTarget = RuntimeTarget,
            WasmtimeVersion = WasmtimeVersion,
            RuntimesRootFound = RuntimesRootFound,
            Runtimes = Runtimes.Select(r => r.Clone()).ToList()
        };
    }

    #endregion

    #region Properties

    /// <summary>Resolved runtime target of the node process, e.g. "windows-x64".</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public string RuntimeTarget { get; set; } = "";

    /// <summary>Version of the wasmtime host binding on the node.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public string WasmtimeVersion { get; set; } = "";

    /// <summary>True when the runtime archive root was located on the node.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public bool RuntimesRootFound { get; set; }

    /// <summary>One entry per known runtime, with availability + hash.</summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(3)]
    public List<VerifyRuntimeInfoData> Runtimes { get; set; } = [];

    #endregion
}
