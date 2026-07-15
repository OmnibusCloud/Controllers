using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// One unit of sandboxed execution: a program (source files + entry point) run under a
/// pinned language runtime inside an isolated WASM store, optionally verified against
/// a suite, within a resource envelope. Small input → large compute → small output.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifyTaskData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifyTaskData other)
            return false;

        if (Sources.Count != other.Sources.Count)
            return false;

        for (var i = 0; i < Sources.Count; i++)
        {
            if (!Sources[i].Is(other.Sources[i], tolerance))
                return false;
        }

        return TaskIndex.Is(other.TaskIndex)
               && RuntimeId.Is(other.RuntimeId)
               && EntryPoint.Is(other.EntryPoint)
               && Args.Is(other.Args)
               && Stdin.Is(other.Stdin)
               && RandomSeed.Is(other.RandomSeed)
               && Suite.Check(other.Suite)
               && Limits.Check(other.Limits);
    }

    public override VerifyTaskData Clone()
    {
        return new VerifyTaskData
        {
            TaskIndex = TaskIndex,
            RuntimeId = RuntimeId,
            Sources = Sources.Select(s => s.Clone()).ToList(),
            EntryPoint = EntryPoint,
            Args = [.. Args],
            Stdin = Stdin,
            RandomSeed = RandomSeed,
            Suite = Suite?.Clone(),
            Limits = Limits?.Clone()
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Task index for re-keying results (fan-out returns them in completion order,
    /// never source order).
    /// </summary>
    [ToString]
    [MemoryPackOrder(0)]
    public int TaskIndex { get; set; }

    /// <summary>
    /// Pinned runtime identifier (e.g. "python-3.14.6", "quickjs-0.15.1"); resolved
    /// against the node's hash-verified runtime catalog.
    /// </summary>
    [ToString]
    [MemoryPackOrder(1)]
    public string RuntimeId { get; set; } = "";

    /// <summary>Source files materialized into the read-only /task directory.</summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(2)]
    public List<VerifySourceFileData> Sources { get; set; } = [];

    /// <summary>Name of the source file to execute (must be one of <see cref="Sources"/>).</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public string EntryPoint { get; set; } = "";

    /// <summary>Command-line arguments for the suite-less run (suite cases carry their own).</summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(4)]
    public List<string> Args { get; set; } = [];

    /// <summary>Standard input for the suite-less run; null feeds nothing.</summary>
    [MemoryPackOrder(5)]
    public string? Stdin { get; set; }

    /// <summary>
    /// Seed for the sandbox's deterministic random_get stream — part of what makes a
    /// task's execution reproducible bit-for-bit on any node.
    /// </summary>
    [MemoryPackOrder(6)]
    public long RandomSeed { get; set; }

    /// <summary>The specification to verify against; null means "run and record".</summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(7)]
    public VerifySuiteData? Suite { get; set; }

    /// <summary>Per-task limit overrides; unset fields fall back to the batch defaults.</summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(8)]
    public VerifyLimitsData? Limits { get; set; }

    #endregion
}
