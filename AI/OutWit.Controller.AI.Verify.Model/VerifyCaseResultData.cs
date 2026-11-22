using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// Outcome of one suite case execution.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifyCaseResultData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifyCaseResultData other)
            return false;

        return CaseIndex.Is(other.CaseIndex)
               && Passed.Is(other.Passed)
               && Verdict.Is(other.Verdict)
               && ExitCode.Is(other.ExitCode)
               && ActualStdout.Is(other.ActualStdout);
    }

    public override VerifyCaseResultData Clone()
    {
        return new VerifyCaseResultData
        {
            CaseIndex = CaseIndex,
            Passed = Passed,
            Verdict = Verdict,
            ExitCode = ExitCode,
            ActualStdout = ActualStdout
        };
    }

    #endregion

    #region Properties

    /// <summary>Index of the case within the task's suite.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public int CaseIndex { get; set; }

    /// <summary>True when the case executed cleanly and matched its expectations.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public bool Passed { get; set; }

    /// <summary>Execution verdict for this case's run (a case can time out on its own).</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public VerifyVerdict Verdict { get; set; }

    /// <summary>Exit code of this case's run.</summary>
    [MemoryPackOrder(3)]
    public int ExitCode { get; set; }

    /// <summary>Actual stdout of this case's run, capped by the task's stdout limit.</summary>
    [MemoryPackOrder(4)]
    public string ActualStdout { get; set; } = "";

    #endregion
}
