using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// One source file of a task, materialized into the sandbox's read-only /task
/// directory before execution. Content is inline — kilobyte-scale generated
/// programs are the design center (blob-ref indirection can be appended later).
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifySourceFileData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifySourceFileData other)
            return false;

        return Name.Is(other.Name)
               && Content.Is(other.Content);
    }

    public override VerifySourceFileData Clone()
    {
        return new VerifySourceFileData
        {
            Name = Name,
            Content = Content
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// File name relative to the task directory. Plain names only — path
    /// separators and traversal are rejected by the sandbox at materialization.
    /// </summary>
    [ToString]
    [MemoryPackOrder(0)]
    public string Name { get; set; } = "";

    /// <summary>UTF-8 source text.</summary>
    [MemoryPackOrder(1)]
    public string Content { get; set; } = "";

    #endregion
}
