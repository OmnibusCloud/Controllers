namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>What one scan of a module's shape has counted so far (<see cref="VerifyModuleShape"/>).</summary>
internal sealed class VerifyModuleShapeTally
{
    #region Functions

    /// <summary>The module's own functions that leave it, and the values of their types together.</summary>
    /// <param name="values">The parameters and the results of their types, summed.</param>
    /// <returns>The functions.</returns>
    public long EscapingFunctions(out long values)
    {
        values = 0;
        long functions = 0;
        foreach (var function in Escaping)
        {
            if (function < (uint)ImportTypes.Count)
                continue;

            var own = function - (uint)ImportTypes.Count;
            if (own >= (uint)FunctionTypes.Count)
                continue;

            functions++;
            var type = FunctionTypes[(int)own];
            if (type >= 0 && type < Types.Count)
                values += Types[type].Parameters + Types[type].Results;
        }

        return functions;
    }

    /// <summary>The type of a function a call names: imported functions first, then the module's own.</summary>
    /// <param name="function">The function's index among all functions.</param>
    /// <returns>The index of its type, or -1.</returns>
    public int TypeOfFunction(uint function)
    {
        if (function < (uint)ImportTypes.Count)
            return ImportTypes[(int)function];

        var own = function - (uint)ImportTypes.Count;
        return own < (uint)FunctionTypes.Count ? FunctionTypes[(int)own] : -1;
    }

    /// <summary>The units of a call of a type (VerifyCompileMemory.CallUnits).</summary>
    /// <param name="indirect">Whether the call is through a table.</param>
    /// <param name="type">The index of the type called.</param>
    /// <returns>Units.</returns>
    public long CallUnits(bool indirect, int type)
    {
        var (parameters, results) = type >= 0 && type < Types.Count ? Types[type] : (0, 0);
        return VerifyCompileMemory.CallUnits(indirect, parameters, results);
    }

    #endregion

    #region Properties

    public List<(int Parameters, int Results)> Types { get; } = [];

    public List<int> FunctionTypes { get; } = [];

    public List<int> ImportTypes { get; } = [];

    public long KeptUnits { get; set; }

    public long LargestWork { get; set; }

    public long FunctionWork { get; set; }

    public long Imports { get; set; }

    public long TypeValues { get; set; }

    public long Exports { get; set; }

    public HashSet<uint> Escaping { get; } = [];

    public long Cost { get; set; }

    public int DeepestNesting { get; set; }

    public int WidestParameters { get; set; }

    public int WidestResults { get; set; }

    public int WidestBlock { get; set; }

    public int MostLocals { get; set; }

    public long Locals { get; set; }

    public int LargestBody { get; set; }

    public long MostBranches { get; set; }

    public long MostBlockUnits { get; set; }

    public bool HasStartFunction { get; set; }

    #endregion
}
