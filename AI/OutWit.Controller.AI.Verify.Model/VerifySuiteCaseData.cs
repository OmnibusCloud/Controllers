using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// One test case of a suite: the program is executed once per case with the case's
/// stdin/args, and its stdout / exit code are compared against the expectation.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifySuiteCaseData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifySuiteCaseData other)
            return false;

        return Stdin.Is(other.Stdin)
               && Args.Is(other.Args)
               && ExpectedStdout.Is(other.ExpectedStdout)
               && ExpectedExitCode.Is(other.ExpectedExitCode);
    }

    public override VerifySuiteCaseData Clone()
    {
        return new VerifySuiteCaseData
        {
            Stdin = Stdin,
            Args = [.. Args],
            ExpectedStdout = ExpectedStdout,
            ExpectedExitCode = ExpectedExitCode
        };
    }

    #endregion

    #region Properties

    /// <summary>Standard input fed to the program for this case; null feeds nothing.</summary>
    [MemoryPackOrder(0)]
    public string? Stdin { get; set; }

    /// <summary>Command-line arguments appended after the entry point for this case.</summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(1)]
    public List<string> Args { get; set; } = [];

    /// <summary>
    /// Expected stdout, compared byte-exact (outputs are deterministic by
    /// construction). Null skips the stdout comparison for this case.
    /// </summary>
    [MemoryPackOrder(2)]
    public string? ExpectedStdout { get; set; }

    /// <summary>Expected exit code (0 for the common "program succeeds" case).</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public int ExpectedExitCode { get; set; }

    #endregion
}
