namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// What becomes of the proof that a host under hardware stack protection survives a guest's
/// trap, on the machine the tests run on (<see cref="VerifyStackProtectionProof"/>).
/// </summary>
internal enum VerifyProofRun
{
    /// <summary>The trap host ran under shadow stacks: that it survived is the proof.</summary>
    Runs,

    /// <summary>The system has no shadow stacks (Linux, macOS): skipped, on a developer's machine and on the build server alike.</summary>
    NotOnThisSystem,

    /// <summary>A developer's Windows that does not enforce shadow stacks: skipped, with the reason.</summary>
    NotOnThisMachine,

    /// <summary>The build server's Windows did not enforce shadow stacks: a failure, with the reason.</summary>
    LostOnTheBuildServer
}
