namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>The bounds one scan of a module's shape holds it to (<see cref="VerifyModuleShape"/>).</summary>
/// <param name="Nesting">The deepest blocks may nest.</param>
/// <param name="Parameters">The most parameters of a function type.</param>
/// <param name="Results">The most results of a function type.</param>
/// <param name="BlockValues">The most values a block takes and gives.</param>
/// <param name="Locals">The most locals of a function.</param>
/// <param name="ModuleLocals">The most locals the functions declare together.</param>
/// <param name="Functions">The most functions.</param>
/// <param name="BodyBytes">The largest body of a function.</param>
/// <param name="StartAllowed">Whether a start function is allowed.</param>
/// <param name="CompileMemory">The most a compile may be estimated to take, bytes.</param>
/// <param name="Types">The most function types.</param>
/// <param name="Imports">The most imports.</param>
/// <param name="Exports">The most exports.</param>
internal readonly record struct VerifyModuleShapeBounds(int Nesting, int Parameters, int Results, int BlockValues, int Locals, long ModuleLocals, int Functions, int BodyBytes,
    bool StartAllowed, long CompileMemory, long Types, long Imports, long Exports);
