namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>What the scan of a module's shape came to.</summary>
/// <param name="CompileFuel">What compiling the module costs the task, in fuel.</param>
/// <param name="Problem">Why the module is not compiled at all, in fixed words; null when it may be.</param>
internal sealed record VerifyModuleShapeReport(long CompileFuel, string? Problem)
{
    /// <summary>The deepest blocks, loops and ifs nest in any function the scan read.</summary>
    public int DeepestNesting { get; init; }

    /// <summary>The part of the price that is the shape's and not the bytes': what the loops and the branch tables add.</summary>
    public long ShapeFuel { get; init; }
}
