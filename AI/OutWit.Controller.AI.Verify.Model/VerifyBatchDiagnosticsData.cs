using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// What a node reports with every result batch: the kind of system it is, and with which
/// engine, controller and runtime module it ran the batch. It says nothing of which machine
/// that is, nor of how the machine ran the batch: the members that could are kept for the wire
/// layout and leave a node empty. It rides next to the results so that provenance survives a
/// failed run; it stays out of the verdict report, which must not depend on where a task ran.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifyBatchDiagnosticsData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifyBatchDiagnosticsData other)
            return false;

        return NodeId.Is(other.NodeId)
               && NodeName.Is(other.NodeName)
               && RuntimeTarget.Is(other.RuntimeTarget)
               && WasmtimeVersion.Is(other.WasmtimeVersion)
               && NativeLibraryPath.Is(other.NativeLibraryPath)
               && RuntimeId.Is(other.RuntimeId)
               && RuntimeSha256.Is(other.RuntimeSha256)
               && Degree.Is(other.Degree)
               && TaskCount.Is(other.TaskCount)
               && WallMs.Is(other.WallMs)
               && ControllerVersion.Is(other.ControllerVersion);
    }

    public override VerifyBatchDiagnosticsData Clone()
    {
        return new VerifyBatchDiagnosticsData
        {
            NodeId = NodeId,
            NodeName = NodeName,
            RuntimeTarget = RuntimeTarget,
            WasmtimeVersion = WasmtimeVersion,
            NativeLibraryPath = NativeLibraryPath,
            RuntimeId = RuntimeId,
            RuntimeSha256 = RuntimeSha256,
            Degree = Degree,
            TaskCount = TaskCount,
            WallMs = WallMs,
            ControllerVersion = ControllerVersion
        };
    }

    #endregion

    #region Properties

    /// <summary>Not filled: a batch does not say which machine ran it. Kept for the wire layout.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public Guid NodeId { get; set; }

    /// <summary>Not filled: a batch does not say which machine ran it. Kept for the wire layout.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public string NodeName { get; set; } = "";

    /// <summary>OS and architecture, e.g. <c>linux-x64</c>.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public string RuntimeTarget { get; set; } = "";

    /// <summary>The wasmtime binding version the node runs.</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public string WasmtimeVersion { get; set; } = "";

    /// <summary>Not filled: a path on the node can name its account. Kept for the wire layout.</summary>
    [MemoryPackOrder(4)]
    public string NativeLibraryPath { get; set; } = "";

    /// <summary>The batch's runtime id.</summary>
    [ToString]
    [MemoryPackOrder(5)]
    public string RuntimeId { get; set; } = "";

    /// <summary>The SHA-256 the runtime module was verified against (empty when it was unavailable).</summary>
    [MemoryPackOrder(6)]
    public string RuntimeSha256 { get; set; } = "";

    /// <summary>
    /// Concurrent sandboxes the batch ran at. Known on the node only: a result package carries 0
    /// (with the memory cap, the degree gives a node's cores and free memory).
    /// </summary>
    [ToString]
    [MemoryPackOrder(7)]
    public int Degree { get; set; }

    /// <summary>Tasks in the batch.</summary>
    [ToString]
    [MemoryPackOrder(8)]
    public int TaskCount { get; set; }

    /// <summary>Wall time of the whole batch on the node. Known on the node only: a result package carries 0.</summary>
    [ToString]
    [MemoryPackOrder(9)]
    public int WallMs { get; set; }

    /// <summary>The controller version the node ran.</summary>
    [ToString]
    [MemoryPackOrder(10)]
    public string ControllerVersion { get; set; } = "";

    #endregion
}
