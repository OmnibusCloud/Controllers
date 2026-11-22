namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// What the engine's compiler takes in memory for a module, read off its shape before it is
/// compiled (<see cref="VerifyModuleShape"/>), and the most a module's compile may take. The
/// compiler takes memory for the function it works on; it keeps what it made of every function
/// (its code, and what the engine must know of it: where it calls, where it may trap, where its
/// branches go) until the whole module is done; and it compiles code of its own for every function
/// type and for every function that leaves the module. The first grows with the function at hand,
/// the second with the instructions of all of them, by their kind, the third with the types and
/// the escaping functions.
/// <para>
/// The figures were fitted to measurements of the engine's compiler (wasmtime 48.0.8, one
/// thread) on an x64 Windows node in both of its trap modes and on an arm64 macOS node, taking
/// the largest and a margin of at least 15 %: one formula for every node, since what a module
/// may take must not depend on the machine that happens to compile it. An instruction is counted
/// by its family (integer, float, vector, memory, branch...) at the most any measured instruction
/// of the family took, and an instruction of no family at the most of any: an instruction nobody
/// measured is never counted lower than one that was. A module estimated over
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

    /// <summary>What the engine compiles for a function type of the module, whatever uses it, bytes.</summary>
    public const long PER_TYPE = 14 << 10;

    /// <summary>What it compiles besides for every value of a function type, a parameter or a result, bytes.</summary>
    public const long PER_TYPE_VALUE = 320;

    /// <summary>What the engine compiles for a function of the module that leaves it (exported, in a table, or named by <c>ref.func</c>), bytes.</summary>
    public const long PER_ESCAPING_FUNCTION = 19 << 10;

    /// <summary>What it compiles besides for every value of such a function's type, a parameter or a result, bytes.</summary>
    public const long PER_ESCAPING_VALUE = 352;

    /// <summary>What the engine holds for an import, bytes.</summary>
    public const long PER_IMPORT = 4 << 10;

    /// <summary>What the engine holds for an export, bytes.</summary>
    public const long PER_EXPORT = 2 << 10;

    /// <summary>
    /// The most a module's compile may take, bytes. A module estimated over it is refused before it
    /// is compiled; a node sets this much aside, once, for the compiles of the larger kind.
    /// </summary>
    public const long MAX_BYTES = 1536L << 20;

    /// <summary>The units of an integer instruction: arithmetic, a comparison, a constant, a conversion between integers, a select, a return.</summary>
    public const int UNITS_PLAIN = 3;

    /// <summary>The units of a float instruction: arithmetic, a comparison, a constant, a conversion to or from a float.</summary>
    public const int UNITS_FLOAT = 30;

    /// <summary>The units of a vector instruction other than a load or a store.</summary>
    public const int UNITS_VECTOR = 34;

    /// <summary>The units of a conversion of a float to an integer that saturates (no trap).</summary>
    public const int UNITS_SATURATING = 40;

    /// <summary>The units of a load or a store, and of a read or a write of a global.</summary>
    public const int UNITS_MEMORY = 40;

    /// <summary>The units of a branch (<c>br</c>, <c>br_if</c>, <c>if</c>, every target of <c>br_table</c>).</summary>
    public const int UNITS_BRANCH = 11;

    /// <summary>The units of a loop: the engine checks the fuel and the clock at its head.</summary>
    public const int UNITS_LOOP = 80;

    /// <summary>The units of <c>unreachable</c>, a trap.</summary>
    public const int UNITS_TRAP = 28;

    /// <summary>The units of a division or a remainder, and the places where it may trap.</summary>
    public const int UNITS_DIVISION = 68;

    /// <summary>The units of a conversion of a float to an integer that may trap.</summary>
    public const int UNITS_TRUNCATION = 112;

    /// <summary>The units of a call of a function of the module or of an import, besides the values it passes.</summary>
    public const int UNITS_CALL = 11;

    /// <summary>The units of a call through a table, besides the values it passes.</summary>
    public const int UNITS_INDIRECT_CALL = 100;

    /// <summary>
    /// The units of an instruction that calls into the engine or reads its state: growing the memory
    /// and asking its size, copying or filling it, the instructions of tables, <c>ref.func</c> and
    /// the other reference instructions.
    /// </summary>
    public const int UNITS_ENGINE_CALL = 48;

    /// <summary>The units of a value a call passes: a parameter or a result of the type called.</summary>
    public const int UNITS_PER_VALUE = 1;

    /// <summary>The units of an instruction of no family above: the most of any.</summary>
    public const int UNITS_UNCLASSIFIED = UNITS_TRUNCATION;

    /// <summary>What the compiler takes besides its bytes, while it works on a function, for a load or a store in it.</summary>
    public const long WORK_MEMORY = 16 << 10;

    /// <summary>What the compiler takes besides, while it works on a function, for a division or a remainder in it.</summary>
    public const long WORK_DIVISION = 16 << 10;

    /// <summary>What the compiler takes besides, while it works on a function, for a conversion in it that may trap.</summary>
    public const long WORK_TRUNCATION = 40 << 10;

    /// <summary>What the compiler takes besides, while it works on a function, for a call through a table in it.</summary>
    public const long WORK_INDIRECT_CALL = 48 << 10;

    /// <summary>What the compiler takes besides, while it works on a function, for a loop in it.</summary>
    public const long WORK_LOOP = 60 << 10;

    /// <summary>What the compiler takes besides, while it works on a function, for a call into the engine in it.</summary>
    public const long WORK_ENGINE_CALL = 24 << 10;

    /// <summary>What the compiler takes besides, while it works on a function, for a float instruction in it.</summary>
    public const long WORK_FLOAT = 12 << 10;

    /// <summary>What the compiler takes besides, while it works on a function, for a vector instruction in it.</summary>
    public const long WORK_VECTOR = 8 << 10;

    /// <summary>What the compiler takes besides, while it works on a function, for an instruction of no family: the most of any.</summary>
    public const long WORK_UNCLASSIFIED = WORK_LOOP;

    #endregion

    #region Functions

    /// <summary>The estimate: what compiling a module of the shape given may take, bytes.</summary>
    /// <param name="moduleBytes">The module's size.</param>
    /// <param name="functions">The functions it defines.</param>
    /// <param name="largestWork">The most any one function takes while it is compiled (<see cref="Work"/>).</param>
    /// <param name="blockUnits">The most units of blocks that pass values in one function.</param>
    /// <param name="keptUnits">The units of what all its functions do (<see cref="Units"/>, <see cref="CallUnits"/>), summed.</param>
    /// <param name="surface">What the engine compiles and holds for the module's types, imports, exports and escaping functions (<see cref="Surface"/>).</param>
    /// <returns>Bytes, saturating.</returns>
    public static long Estimate(long moduleBytes, long functions, long largestWork, long blockUnits, long keptUnits, long surface)
    {
        var memory = BASE_BYTES;
        memory = Add(memory, Times(moduleBytes, PER_MODULE_BYTE));
        memory = Add(memory, Times(functions, PER_FUNCTION));
        memory = Add(memory, largestWork);
        memory = Add(memory, Times(blockUnits, PER_BLOCK_UNIT));
        memory = Add(memory, Times(keptUnits, PER_KEPT_UNIT));
        return Add(memory, surface);
    }

    /// <summary>What the engine compiles and holds for a module besides its functions' bodies, bytes.</summary>
    /// <param name="types">The function types.</param>
    /// <param name="typeValues">The parameters and results of all of them, summed.</param>
    /// <param name="imports">The imports.</param>
    /// <param name="exports">The exports.</param>
    /// <param name="escapingFunctions">The module's own functions that leave it.</param>
    /// <param name="escapingValues">The parameters and results of their types, summed.</param>
    /// <returns>Bytes, saturating.</returns>
    public static long Surface(long types, long typeValues, long imports, long exports, long escapingFunctions, long escapingValues)
    {
        var memory = Times(types, PER_TYPE);
        memory = Add(memory, Times(typeValues, PER_TYPE_VALUE));
        memory = Add(memory, Times(imports, PER_IMPORT));
        memory = Add(memory, Times(exports, PER_EXPORT));
        memory = Add(memory, Times(escapingFunctions, PER_ESCAPING_FUNCTION));
        return Add(memory, Times(escapingValues, PER_ESCAPING_VALUE));
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
    /// The units an instruction other than a call or <c>br_table</c> counts by its family: nothing for
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
            Kind.Plain => UNITS_PLAIN,
            Kind.Float => UNITS_FLOAT,
            Kind.Vector => UNITS_VECTOR,
            Kind.Saturating => UNITS_SATURATING,
            Kind.Memory => UNITS_MEMORY,
            Kind.Branch => UNITS_BRANCH,
            Kind.Loop => UNITS_LOOP,
            Kind.Trap => UNITS_TRAP,
            Kind.Division => UNITS_DIVISION,
            Kind.Truncation => UNITS_TRUNCATION,
            Kind.EngineCall => UNITS_ENGINE_CALL,
            _ => UNITS_UNCLASSIFIED
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
            Kind.Free or Kind.Plain or Kind.Branch or Kind.Trap or Kind.Saturating => 0,
            Kind.Float => WORK_FLOAT,
            Kind.Vector => WORK_VECTOR,
            Kind.Memory => WORK_MEMORY,
            Kind.Loop => WORK_LOOP,
            Kind.Division => WORK_DIVISION,
            Kind.Truncation => WORK_TRUNCATION,
            Kind.EngineCall => WORK_ENGINE_CALL,
            _ => WORK_UNCLASSIFIED
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
            case 0x00: // unreachable
                return Kind.Trap;
            case 0x01: // nop
            case 0x02: // block
            case 0x05: // else
            case 0x0B: // end
            case 0x1A: // drop
            case 0x20: // local.get
            case 0x21: // local.set
            case 0x22: // local.tee
                return Kind.Free;
            case 0x03: // loop
                return Kind.Loop;
            case 0x04: // if
            case 0x0C: // br
            case 0x0D: // br_if
                return Kind.Branch;
            case 0x0F: // return
            case 0x1B: // select
            case 0x1C: // select with its type
            case 0x41: // i32.const
            case 0x42: // i64.const
            case >= 0x45 and <= 0x5A: // integer comparisons
            case >= 0x67 and <= 0x6C: // i32 clz, ctz, popcnt, add, sub, mul
            case >= 0x71 and <= 0x7E: // i32 and .. rotr, i64 clz .. mul
            case >= 0x83 and <= 0x8A: // i64 and .. rotr
            case 0xA7: // i32.wrap_i64
            case 0xAC: // i64.extend_i32_s
            case 0xAD: // i64.extend_i32_u
            case >= 0xC0 and <= 0xC4: // sign extensions
                return Kind.Plain;
            case >= 0x6D and <= 0x70: // i32.div_s, i32.div_u, i32.rem_s, i32.rem_u
            case >= 0x7F and <= 0x82: // i64.div_s, i64.div_u, i64.rem_s, i64.rem_u
                return Kind.Division;
            case 0x43: // f32.const
            case 0x44: // f64.const
            case >= 0x5B and <= 0x66: // float comparisons
            case >= 0x8B and <= 0xA6: // float arithmetic
            case >= 0xB2 and <= 0xBF: // conversions to floats, demotion, promotion, reinterpretations
                return Kind.Float;
            case >= 0xA8 and <= 0xAB: // i32.trunc_f32_s .. i32.trunc_f64_u
            case >= 0xAE and <= 0xB1: // i64.trunc_f32_s .. i64.trunc_f64_u
                return Kind.Truncation;
            case 0x23: // global.get
            case 0x24: // global.set
            case >= 0x28 and <= 0x3E: // loads and stores
                return Kind.Memory;
            case 0x25: // table.get
            case 0x26: // table.set
            case 0x3F: // memory.size
            case 0x40: // memory.grow
            case 0xD0: // ref.null
            case 0xD1: // ref.is_null
            case 0xD2: // ref.func
                return Kind.EngineCall;
            case 0xFC:
                // The conversions that saturate; then the data, memory and table instructions, which call into the engine.
                return sub <= 7 ? Kind.Saturating : Kind.EngineCall;
            case 0xFD:
                // Loads and stores of vectors and of their lanes; the rest of the vector instructions.
                return sub is <= 11 or 92 or 93 or (>= 84 and <= 91) ? Kind.Memory : Kind.Vector;
            default:
                return Kind.Unclassified;
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

    /// <summary>The families of instructions the estimate tells apart.</summary>
    private enum Kind
    {
        Free,
        Plain,
        Float,
        Vector,
        Saturating,
        Memory,
        Branch,
        Loop,
        Trap,
        Division,
        Truncation,
        EngineCall,
        Unclassified
    }

    #endregion
}
