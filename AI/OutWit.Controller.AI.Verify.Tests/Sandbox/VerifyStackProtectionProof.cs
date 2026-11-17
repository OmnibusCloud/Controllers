namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// What becomes of the proof that a host under hardware stack protection survives a guest's
/// trap (audit finding V:M-7) on the machine the tests run on.
/// <para>
/// The proof needs a process that really runs under shadow stacks, and only a Windows whose
/// processor and system enforce them gives one. A developer's machine may not: there the proof
/// is skipped and says so. The build server's Windows run is the one place the proof is made
/// for every change, so a run of it without shadow stacks is a failure and not a skip: a runner
/// image that stops enforcing them is learnt of on the day it happens, not from a line among
/// the skipped tests. Linux and macOS have no shadow stacks and skip everywhere.
/// </para>
/// </summary>
internal static class VerifyStackProtectionProof
{
    #region Constants

    /// <summary>Why the proof does not run on a system that is not Windows.</summary>
    public const string NOT_ON_THIS_SYSTEM =
        "hardware-enforced stack protection (CET shadow stacks) is Windows': the proof does not run on this system";

    /// <summary>Why the proof did not run on a Windows machine that does not enforce shadow stacks.</summary>
    public const string NOT_ENFORCED =
        "the trap host ran WITHOUT shadow stacks on this machine (its processor or its system does not enforce them): " +
        "that a host under hardware stack protection survives a guest's trap was NOT proven here. " +
        "The explicit-trap mode such a host runs in is still exercised (M7ExplicitTrapsGiveTheVerdictsOfHardwareTrapsTest).";

    /// <summary>The same reason on the build server, where it fails the run.</summary>
    public const string LOST_ON_THE_BUILD_SERVER =
        NOT_ENFORCED + " On the build server this is a failure and not a skip: its Windows run is the one place the proof is made, " +
        "and the proof is no longer made there.";

    #endregion

    #region Functions

    /// <summary>Decides what becomes of the proof.</summary>
    /// <param name="windows">Whether the tests run on Windows.</param>
    /// <param name="shadowStacks">Whether the trap host said that its process runs under shadow stacks.</param>
    /// <param name="onTheBuildServer">Whether the tests run on the build server.</param>
    /// <returns>Whether the proof runs, is skipped or fails the run.</returns>
    public static VerifyProofRun Of(bool windows, bool shadowStacks, bool onTheBuildServer)
    {
        if (!windows)
            return VerifyProofRun.NotOnThisSystem;

        if (shadowStacks)
            return VerifyProofRun.Runs;

        return onTheBuildServer ? VerifyProofRun.LostOnTheBuildServer : VerifyProofRun.NotOnThisMachine;
    }

    /// <summary>The reason a proof that does not run gives.</summary>
    /// <param name="run">What became of the proof.</param>
    /// <returns>The reason, or null for a proof that runs.</returns>
    public static string? Reason(VerifyProofRun run)
    {
        return run switch
        {
            VerifyProofRun.NotOnThisSystem => NOT_ON_THIS_SYSTEM,
            VerifyProofRun.NotOnThisMachine => NOT_ENFORCED,
            VerifyProofRun.LostOnTheBuildServer => LOST_ON_THE_BUILD_SERVER,
            _ => null
        };
    }

    #endregion
}
