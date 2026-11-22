namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>What the scan of a module's shape came to.</summary>
/// <param name="CompileFuel">What compiling the module costs the task, in fuel.</param>
/// <param name="Problem">Why the module is not compiled at all, in fixed words; null when it may be.</param>
internal sealed record VerifyModuleShapeReport(long CompileFuel, string? Problem)
{
    /// <summary>The deepest blocks, loops and ifs nest in any function the scan read.</summary>
    public int DeepestNesting { get; init; }

    /// <summary>The part of the price that is the shape's and not the bytes': what the loops, the branches, the blocks, the locals and the smallest functions add.</summary>
    public long ShapeFuel { get; init; }

    /// <summary>The functions the module defines.</summary>
    public int Functions { get; init; }

    /// <summary>The most parameters any function type of the module has.</summary>
    public int WidestParameters { get; init; }

    /// <summary>The most results any function type of the module has.</summary>
    public int WidestResults { get; init; }

    /// <summary>The most values any block, loop or if takes and gives together.</summary>
    public int WidestBlock { get; init; }

    /// <summary>The most locals any function has, its parameters included.</summary>
    public int MostLocals { get; init; }

    /// <summary>The locals the functions declare together.</summary>
    public long Locals { get; init; }

    /// <summary>The largest body of a function, bytes.</summary>
    public int LargestBody { get; init; }

    /// <summary>The most branches any function has.</summary>
    public long MostBranches { get; init; }

    /// <summary>Whether the module has a start function: code the engine runs when it makes the module ready, before <c>_start</c>.</summary>
    public bool HasStartFunction { get; init; }

    /// <summary>The units of what the functions do, summed (VerifyCompileMemory.Units): what the compiler keeps of them until the module is done grows with it.</summary>
    public long KeptUnits { get; init; }

    /// <summary>What the compiler takes for the function that takes the most while it is worked on, bytes (VerifyCompileMemory.Work).</summary>
    public long LargestWork { get; init; }

    /// <summary>What the engine compiles and holds for the module's types, imports, exports and escaping functions, bytes (VerifyCompileMemory.Surface).</summary>
    public long SurfaceBytes { get; init; }

    /// <summary>What the engine's compiler may take while it compiles the module, bytes: an estimate from the shape.</summary>
    public long CompileMemoryBytes { get; init; }
}
