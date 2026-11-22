namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// What the engine's compiler takes in memory for a module, read off its shape before it is
/// compiled (<see cref="VerifyModuleShape"/>), and the most a module's compile may take. The
/// compiler takes memory for the function it works on, and it keeps what it made of every
/// function (its code, and what the engine must know of it: where it calls, where it may trap,
/// where its branches go) until the whole module is done. The first grows with the function at
/// hand, the second with the instructions of all of them, by their kind.
/// <para>
/// The figures were fitted to measurements of the engine's compiler (wasmtime 48.0.8, one
/// thread) on an x64 Windows node in both of its trap modes and on an arm64 macOS node, taking
/// the largest and a margin of at least 15 %: one formula for every node, since what a module
/// may take must not depend on the machine that happens to compile it. A module estimated over
/// <see cref="MAX_BYTES"/> is refused before it is compiled, so whatever is compiled fits the
/// memory a node sets aside for one compile (the gate, <see cref="VerifyCompileGate"/>). The
/// figures hold for the kinds of instructions that were measured; they are not a proof about
/// every module the engine accepts.
/// </para>
/// </summary>
internal static class VerifyCompileMemory
{
    #region Constants

    /// <summary>What the compiler holds whatever the module, bytes.</summary>
    public const long BASE_BYTES = 16L << 20;

    /// <summary>What the compiler holds a byte of the module, bytes: the module itself.</summary>
    public const long PER_MODULE_BYTE = 2;

    /// <summary>What the compiler holds a function until the module is done, whatever is in it, bytes.</summary>
    public const long PER_FUNCTION = 9 << 10;

    /// <summary>What the compiler takes a byte of the function it works on, bytes.</summary>
    public const long PER_BODY_BYTE = 6 << 10;

    /// <summary>What the compiler takes a unit of a block that passes values, bytes.</summary>
    public const long PER_BLOCK_UNIT = 8;

    /// <summary>What the compiler keeps until the module is done, bytes a unit of what the functions do (<see cref="Units"/>).</summary>
    public const long PER_KEPT_UNIT = 16;

    /// <summary>
    /// The most a module's compile may take, bytes. A module estimated over it is refused before it
    /// is compiled; a node sets this much aside, once, for the compiles of the larger kind.
    /// </summary>
    public const long MAX_BYTES = 2560L << 20;

    /// <summary>The units of an instruction the compiler turns into code of its own: arithmetic, a comparison, a constant.</summary>
    public const int UNITS_PLAIN = 3;

    /// <summary>The units of a load or a store: its code, its check of the address where the node checks it, the record of where it may trap.</summary>
    public const int UNITS_MEMORY = 40;

    /// <summary>The units of a branch (<c>br</c>, <c>br_if</c>, <c>if</c>, every target of <c>br_table</c>).</summary>
    public const int UNITS_BRANCH = 9;

    /// <summary>The units of a division or a remainder, and the places where it may trap.</summary>
    public const int UNITS_DIVISION = 68;

    /// <summary>The units of a conversion of a float to an integer that may trap.</summary>
    public const int UNITS_TRUNCATION = 112;

    /// <summary>The units of a call of a function of the module or of an import, besides the values it passes.</summary>
    public const int UNITS_CALL = 11;

    /// <summary>The units of a call through a table, besides the values it passes.</summary>
    public const int UNITS_INDIRECT_CALL = 100;

    /// <summary>The units of a call into the engine: growing the memory, copying or filling it, the instructions of tables.</summary>
    public const int UNITS_ENGINE_CALL = 11;

    /// <summary>The units of a value a call passes: a parameter or a result of the type called.</summary>
    public const int UNITS_PER_VALUE = 1;

    /// <summary>What the compiler takes besides its bytes, while it works on a function, for a load or a store in it.</summary>
    public const long WORK_MEMORY = 16 << 10;

    /// <summary>What the compiler takes besides, while it works on a function, for a division or a remainder in it.</summary>
    public const long WORK_DIVISION = 16 << 10;

    /// <summary>What the compiler takes besides, while it works on a function, for a conversion in it that may trap.</summary>
    public const long WORK_TRUNCATION = 40 << 10;

    /// <summary>What the compiler takes besides, while it works on a function, for a call through a table in it.</summary>
    public const long WORK_INDIRECT_CALL = 48 << 10;

    #endregion

    #region Functions

    /// <summary>The estimate: what compiling a module of the shape given may take, bytes.</summary>
    /// <param name="moduleBytes">The module's size.</param>
    /// <param name="functions">The functions it defines.</param>
    /// <param name="largestWork">The most any one function takes while it is compiled (<see cref="Work"/>).</param>
    /// <param name="blockUnits">The most units of blocks that pass values in one function.</param>
    /// <param name="keptUnits">The units of what all its functions do (<see cref="Units"/>, <see cref="CallUnits"/>), summed.</param>
    /// <returns>Bytes, saturating.</returns>
    public static long Estimate(long moduleBytes, long functions, long largestWork, long blockUnits, long keptUnits)
    {
        var memory = BASE_BYTES;
        memory = Add(memory, Times(moduleBytes, PER_MODULE_BYTE));
        memory = Add(memory, Times(functions, PER_FUNCTION));
        memory = Add(memory, largestWork);
        memory = Add(memory, Times(blockUnits, PER_BLOCK_UNIT));
        return Add(memory, Times(keptUnits, PER_KEPT_UNIT));
    }

    /// <summary>What compiling one function takes while the compiler works on it, bytes.</summary>
    /// <param name="bodyBytes">The size of its body.</param>
    /// <param name="extra">What its instructions take besides (<see cref="WorkOf"/>, <see cref="WORK_INDIRECT_CALL"/>), summed.</param>
    /// <returns>Bytes, saturating.</returns>
    public static long Work(long bodyBytes, long extra)
    {
        return Add(Times(bodyBytes, PER_BODY_BYTE), extra);
    }

    /// <summary>
    /// The units an instruction other than a call or <c>br_table</c> counts by its kind: nothing for
    /// what only moves values between locals and the stack or opens and ends a block.
    /// </summary>
    /// <param name="op">The instruction's first byte.</param>
    /// <param name="sub">For an instruction of the prefixes 0xFC and 0xFD, its second number (<see cref="SubOf"/>); otherwise 0.</param>
    /// <returns>Units.</returns>
    public static int Units(byte op, uint sub)
    {
        return KindOf(op, sub) switch
        {
            Kind.Free => 0,
            Kind.Branch => UNITS_BRANCH,
            Kind.Memory => UNITS_MEMORY,
            Kind.Division => UNITS_DIVISION,
            Kind.Truncation => UNITS_TRUNCATION,
            Kind.EngineCall => UNITS_ENGINE_CALL,
            _ => UNITS_PLAIN
        };
    }

    /// <summary>What an instruction other than a call takes besides its bytes while its function is compiled.</summary>
    /// <param name="op">The instruction's first byte.</param>
    /// <param name="sub">For an instruction of the prefixes 0xFC and 0xFD, its second number; otherwise 0.</param>
    /// <returns>Bytes.</returns>
    public static long WorkOf(byte op, uint sub)
    {
        return KindOf(op, sub) switch
        {
            Kind.Memory => WORK_MEMORY,
            Kind.Division => WORK_DIVISION,
            Kind.Truncation => WORK_TRUNCATION,
            _ => 0
        };
    }

    /// <summary>The second number of an instruction of the prefixes 0xFC and 0xFD, read without moving on; 0 for any other.</summary>
    /// <param name="op">The instruction's first byte, already read.</param>
    /// <param name="body">The function's body.</param>
    /// <param name="position">Where the instruction's immediates start.</param>
    /// <returns>The second number.</returns>
    public static uint SubOf(byte op, ReadOnlySpan<byte> body, int position)
    {
        if (op is not (0xFC or 0xFD))
            return 0;

        var peek = position;
        return VerifyModuleCode.ReadU32(body, ref peek);
    }

    /// <summary>The units of a call, of a function of the module or of an import, or through a table.</summary>
    /// <param name="indirect">Whether the call is through a table.</param>
    /// <param name="parameters">The parameters of the type called.</param>
    /// <param name="results">Its results.</param>
    /// <returns>Units.</returns>
    public static long CallUnits(bool indirect, int parameters, int results)
    {
        return (indirect ? UNITS_INDIRECT_CALL : UNITS_CALL) + (long)UNITS_PER_VALUE * (Math.Max(0, parameters) + Math.Max(0, results));
    }

    private static Kind KindOf(byte op, uint sub)
    {
        switch (op)
        {
            case 0x01: // nop
            case 0x02: // block
            case 0x03: // loop
            case 0x05: // else
            case 0x0B: // end
            case 0x1A: // drop
            case 0x20: // local.get
            case 0x21: // local.set
            case 0x22: // local.tee
                return Kind.Free;
            case 0x04: // if
            case 0x0C: // br
            case 0x0D: // br_if
                return Kind.Branch;
            case 0x25: // table.get
            case 0x26: // table.set
            case 0x40: // memory.grow
                return Kind.EngineCall;
            case >= 0x6D and <= 0x70: // i32.div_s, i32.div_u, i32.rem_s, i32.rem_u
            case >= 0x7F and <= 0x82: // i64.div_s, i64.div_u, i64.rem_s, i64.rem_u
                return Kind.Division;
            case >= 0xA8 and <= 0xAB: // i32.trunc_f32_s .. i32.trunc_f64_u
            case >= 0xAE and <= 0xB1: // i64.trunc_f32_s .. i64.trunc_f64_u
                return Kind.Truncation;
            case >= 0x28 and <= 0x3E: // loads and stores
                return Kind.Memory;
            case 0xFC:
                // memory.init and the instructions after it (data, memory and table instructions) call into the engine.
                return sub >= 8 ? Kind.EngineCall : Kind.Plain;
            case 0xFD:
                // Loads and stores of vectors and of their lanes.
                return sub is <= 11 or 92 or 93 or (>= 84 and <= 91) ? Kind.Memory : Kind.Plain;
            default:
                return Kind.Plain;
        }
    }

    private static long Times(long left, long right)
    {
        if (left <= 0 || right <= 0)
            return 0;

        return left > long.MaxValue / right ? long.MaxValue : left * right;
    }

    private static long Add(long left, long right)
    {
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }

    #endregion

    #region Nested Types

    /// <summary>The kinds of instructions the estimate tells apart.</summary>
    private enum Kind
    {
        Free,
        Plain,
        Branch,
        Memory,
        Division,
        Truncation,
        EngineCall
    }

    #endregion
}
