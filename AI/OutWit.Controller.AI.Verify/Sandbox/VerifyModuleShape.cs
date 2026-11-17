namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Reads the shape of a module a task brought (the raw WASI runtime) or that the compiler built
/// from a task's sources, before the engine compiles it. Compiling is work on the node that no
/// fuel meters and nothing can stop, and for some shapes it is far from linear in the module's
/// size: a function whose locals are read inside nested loops costs the engine's compiler time
/// with the square of the locals times the depth of the loops (6.9 KB took 1.5 s, 28 KB took 81 s
/// and 1.5 GiB), and a branch table over deeply nested blocks grows the same way. So the shape is
/// priced before anything is compiled: the price is taken from the task's fuel budget, a task
/// that cannot pay it is out of fuel without the module ever being compiled, and blocks nested
/// deeper than <see cref="MAX_NESTING"/> are refused. The scan itself is one pass over the code.
/// <para>
/// The prices were fitted to the engine as measured (wasmtime 48.0.8, one thread, a desktop of
/// 2025): the modules the controller ships compile at 0.6 to 0.8 MiB a second, which a byte's
/// price covers at the reference rate; the loop shape takes 1.8 ns a unit and costs 4 units of
/// fuel, 2 ns at that rate. They are an estimate that holds for the shapes that were tried, not
/// a proof about every module the engine accepts.
/// </para>
/// <para>
/// The module was validated by the engine before it comes here, so its structure is sound; an
/// instruction this scan does not know is refused rather than guessed at.
/// </para>
/// </summary>
internal static class VerifyModuleShape
{
    #region Constants

    /// <summary>What a byte of a module costs to compile, in fuel: about a microsecond at the reference rate.</summary>
    public const long COMPILE_FUEL_PER_BYTE = 2048;

    /// <summary>What one unit of a function's loop shape costs, in fuel: (locals)² × (depth of its loops) units.</summary>
    public const long COMPILE_FUEL_PER_LOOP_UNIT = 4;

    /// <summary>What one unit of a branch table costs, in fuel: (its targets) × (the blocks it sits in) units.</summary>
    public const long COMPILE_FUEL_PER_TABLE_UNIT = 100;

    /// <summary>
    /// The deepest blocks, loops and ifs may nest in one function. The interpreters the controller
    /// ships nest 115 (Lua), 316 (QuickJS), 616 (Ruby) and 781 (CPython) deep; the limit is two and
    /// a half times the deepest of them. Past it a branch table over the nest costs the engine's
    /// compiler time with the cube of the depth (20,000 levels: 82 s and 2 GiB).
    /// </summary>
    public const int MAX_NESTING = 2048;

    private const int SECTION_TYPE = 1;
    private const int SECTION_FUNCTION = 3;
    private const int SECTION_CODE = 10;
    private const int HEADER_LENGTH = 8;

    private const byte FORM_FUNCTION = 0x60;

    private const byte OP_BLOCK = 0x02;
    private const byte OP_LOOP = 0x03;
    private const byte OP_IF = 0x04;
    private const byte OP_END = 0x0B;
    private const byte OP_BR_TABLE = 0x0E;

    private const byte KIND_BLOCK = 0;
    private const byte KIND_LOOP = 1;

    private const string UNKNOWN_INSTRUCTION = "the module uses an instruction the sandbox does not take";
    private const string UNREADABLE = "the module's code cannot be read";

    #endregion

    #region Functions

    /// <summary>
    /// Prices a module and checks its nesting.
    /// </summary>
    /// <param name="module">The module's bytes, already validated by the engine.</param>
    /// <returns>What compiling it costs in fuel, or why it is not compiled at all (fixed words).</returns>
    public static VerifyModuleShapeReport Scan(ReadOnlySpan<byte> module)
    {
        return Scan(module, MAX_NESTING);
    }

    /// <summary>
    /// Prices a module and checks its nesting against a limit of the caller's: for measuring
    /// what modules are like.
    /// </summary>
    /// <param name="module">The module's bytes, already validated by the engine.</param>
    /// <param name="maxNesting">The deepest blocks may nest.</param>
    /// <returns>What compiling it costs in fuel, or why it is not compiled at all (fixed words).</returns>
    internal static VerifyModuleShapeReport Scan(ReadOnlySpan<byte> module, int maxNesting)
    {
        var bytes = Saturate(module.Length, COMPILE_FUEL_PER_BYTE);
        var cost = bytes;
        if (module.Length < HEADER_LENGTH)
            return new VerifyModuleShapeReport(cost, UNREADABLE);

        var deepest = 0;

        var types = new List<int>();
        var functionTypes = new List<int>();
        var position = HEADER_LENGTH;

        try
        {
            while (position < module.Length)
            {
                var id = module[position++];
                var size = (int)ReadU32(module, ref position);
                if (size < 0 || size > module.Length - position)
                    return new VerifyModuleShapeReport(cost, UNREADABLE);

                var section = module.Slice(position, size);
                position += size;

                switch (id)
                {
                    case SECTION_TYPE:
                        if (!ReadTypes(section, types))
                            return new VerifyModuleShapeReport(cost, UNKNOWN_INSTRUCTION);
                        break;
                    case SECTION_FUNCTION:
                        ReadFunctionTypes(section, functionTypes);
                        break;
                    case SECTION_CODE:
                        var problem = ScanCode(section, types, functionTypes, maxNesting, ref cost, ref deepest);
                        if (problem != null)
                            return new VerifyModuleShapeReport(cost, problem) { DeepestNesting = deepest };
                        break;
                }
            }
        }
        catch (IndexOutOfRangeException)
        {
            return new VerifyModuleShapeReport(cost, UNREADABLE);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new VerifyModuleShapeReport(cost, UNREADABLE);
        }

        return new VerifyModuleShapeReport(cost, null) { DeepestNesting = deepest, ShapeFuel = cost - bytes };
    }

    private static bool ReadTypes(ReadOnlySpan<byte> section, List<int> parameterCounts)
    {
        var position = 0;
        var count = ReadU32(section, ref position);
        for (var index = 0u; index < count; index++)
        {
            // Function types only: the engine is configured without the proposals that add others.
            if (section[position++] != FORM_FUNCTION)
                return false;

            var parameters = (int)ReadU32(section, ref position);
            position += parameters;
            parameterCounts.Add(parameters);

            var results = (int)ReadU32(section, ref position);
            position += results;
        }

        return true;
    }

    private static void ReadFunctionTypes(ReadOnlySpan<byte> section, List<int> functionTypes)
    {
        var position = 0;
        var count = ReadU32(section, ref position);
        for (var index = 0u; index < count; index++)
            functionTypes.Add((int)ReadU32(section, ref position));
    }

    private static string? ScanCode(ReadOnlySpan<byte> section, List<int> types, List<int> functionTypes, int maxNesting, ref long cost, ref int deepest)
    {
        var position = 0;
        var count = ReadU32(section, ref position);
        var kinds = new byte[maxNesting + 2];

        for (var index = 0; index < count; index++)
        {
            var size = (int)ReadU32(section, ref position);
            var body = section.Slice(position, size);
            position += size;

            var parameters = index < functionTypes.Count && functionTypes[index] >= 0 && functionTypes[index] < types.Count ? types[functionTypes[index]] : 0;
            var problem = ScanFunction(body, parameters, types, kinds, maxNesting, ref cost, ref deepest);
            if (problem != null)
                return problem;
        }

        return null;
    }

    private static string? ScanFunction(ReadOnlySpan<byte> body, int parameters, List<int> types, byte[] kinds, int maxNesting, ref long cost, ref int deepest)
    {
        var position = 0;

        long locals = parameters;
        var groups = ReadU32(body, ref position);
        for (var group = 0u; group < groups; group++)
        {
            locals += ReadU32(body, ref position);
            position++; // the group's value type: one byte without the proposals that widen it
        }

        var depth = 1;
        var loops = 0;
        var deepestLoops = 0;
        long localUses = 0;
        long widestLoop = 0;
        long tableUnits = 0;
        kinds[0] = KIND_BLOCK;

        while (depth > 0)
        {
            var op = body[position++];
            switch (op)
            {
                case OP_BLOCK:
                case OP_LOOP:
                case OP_IF:
                    var blockType = ReadS64(body, ref position);
                    if (depth > maxNesting)
                        return $"a function of the module nests blocks deeper than {maxNesting}";

                    deepest = Math.Max(deepest, depth);

                    if (op == OP_LOOP)
                    {
                        loops++;
                        deepestLoops = Math.Max(deepestLoops, loops);

                        // A loop that takes values takes them as the engine's compiler takes locals.
                        if (blockType >= 0 && blockType < types.Count)
                            widestLoop = Math.Max(widestLoop, types[(int)blockType]);
                    }

                    kinds[depth++] = op == OP_LOOP ? KIND_LOOP : KIND_BLOCK;
                    break;
                case OP_END:
                    if (kinds[--depth] == KIND_LOOP)
                        loops--;
                    break;
                case OP_BR_TABLE:
                    var targets = ReadU32(body, ref position);
                    for (var target = 0u; target <= targets; target++)
                        ReadU32(body, ref position);
                    tableUnits = AddSaturating(tableUnits, Saturate(targets + 1, depth));
                    break;
                case 0x20:
                case 0x21:
                case 0x22:
                    ReadU32(body, ref position);
                    localUses++;
                    break;
                default:
                    if (!SkipImmediates(op, body, ref position))
                        return UNKNOWN_INSTRUCTION;
                    break;
            }
        }

        if (position != body.Length)
            return UNREADABLE;

        var live = Math.Min(locals, localUses) + widestLoop;
        var loopUnits = Saturate(Saturate(live, live), deepestLoops);

        cost = AddSaturating(cost, Saturate(loopUnits, COMPILE_FUEL_PER_LOOP_UNIT));
        cost = AddSaturating(cost, Saturate(tableUnits, COMPILE_FUEL_PER_TABLE_UNIT));
        return null;
    }

    /// <summary>Steps over the immediates of an instruction that neither opens nor closes a block; false for one the scan does not know.</summary>
    private static bool SkipImmediates(byte op, ReadOnlySpan<byte> body, ref int position)
    {
        switch (op)
        {
            case 0x00: // unreachable
            case 0x01: // nop
            case 0x05: // else
            case 0x0F: // return
            case 0x1A: // drop
            case 0x1B: // select
            case 0xD1: // ref.is_null
                return true;
            case 0x0C: // br
            case 0x0D: // br_if
            case 0x10: // call
            case 0x12: // return_call
            case 0x23: // global.get
            case 0x24: // global.set
            case 0x25: // table.get
            case 0x26: // table.set
            case 0x3F: // memory.size
            case 0x40: // memory.grow
            case 0xD2: // ref.func
                ReadU32(body, ref position);
                return true;
            case 0x11: // call_indirect
            case 0x13: // return_call_indirect
                ReadU32(body, ref position);
                ReadU32(body, ref position);
                return true;
            case 0x1C: // select with types
                position += (int)ReadU32(body, ref position);
                return true;
            case 0x41: // i32.const
            case 0x42: // i64.const
            case 0xD0: // ref.null
                ReadS64(body, ref position);
                return true;
            case 0x43: // f32.const
                position += 4;
                return true;
            case 0x44: // f64.const
                position += 8;
                return true;
            case 0xFC:
                return SkipSaturatingAndBulk(body, ref position);
            case 0xFD:
                return SkipVector(body, ref position);
        }

        if (op is >= 0x28 and <= 0x3E)
        {
            SkipMemoryArgument(body, ref position);
            return true;
        }

        // Comparisons, arithmetic, conversions and sign extension: no immediates.
        return op is >= 0x45 and <= 0xC4;
    }

    private static bool SkipSaturatingAndBulk(ReadOnlySpan<byte> body, ref int position)
    {
        var sub = ReadU32(body, ref position);
        switch (sub)
        {
            case <= 7: // saturating conversions
                return true;
            case 9: // data.drop
            case 11: // memory.fill
            case 13: // elem.drop
            case 15: // table.grow
            case 16: // table.size
            case 17: // table.fill
                ReadU32(body, ref position);
                return true;
            case 8: // memory.init
            case 10: // memory.copy
            case 12: // table.init
            case 14: // table.copy
                ReadU32(body, ref position);
                ReadU32(body, ref position);
                return true;
            default:
                return false;
        }
    }

    private static bool SkipVector(ReadOnlySpan<byte> body, ref int position)
    {
        var sub = ReadU32(body, ref position);
        switch (sub)
        {
            case <= 11: // loads and the store
            case 92:
            case 93:
                SkipMemoryArgument(body, ref position);
                return true;
            case 12: // v128.const
            case 13: // i8x16.shuffle
                position += 16;
                return true;
            case >= 21 and <= 34: // lane access
                position++;
                return true;
            case >= 84 and <= 91: // lane loads and stores
                SkipMemoryArgument(body, ref position);
                position++;
                return true;
            default:
                // Everything else of the vector and relaxed vector instructions has no immediates.
                return sub <= 0x113;
        }
    }

    private static void SkipMemoryArgument(ReadOnlySpan<byte> body, ref int position)
    {
        // Bit 6 of the alignment says a memory index follows (a module with one memory never sets it).
        var alignment = ReadU32(body, ref position);
        if ((alignment & 0x40) != 0)
            ReadU32(body, ref position);

        ReadU64(body, ref position);
    }

    private static uint ReadU32(ReadOnlySpan<byte> bytes, ref int position)
    {
        return (uint)ReadU64(bytes, ref position);
    }

    private static ulong ReadU64(ReadOnlySpan<byte> bytes, ref int position)
    {
        ulong value = 0;
        for (var shift = 0; shift < 70; shift += 7)
        {
            var part = bytes[position++];
            if (shift < 64)
                value |= (ulong)(part & 0x7F) << shift;

            if ((part & 0x80) == 0)
                return value;
        }

        throw new ArgumentOutOfRangeException(nameof(bytes), "a number of the module is longer than ten bytes");
    }

    private static long ReadS64(ReadOnlySpan<byte> bytes, ref int position)
    {
        long value = 0;
        for (var shift = 0; shift < 70; shift += 7)
        {
            var part = bytes[position++];
            if (shift < 64)
                value |= (long)(part & 0x7F) << shift;

            if ((part & 0x80) != 0)
                continue;

            if (shift < 57 && (part & 0x40) != 0)
                value |= -1L << (shift + 7);

            return value;
        }

        throw new ArgumentOutOfRangeException(nameof(bytes), "a number of the module is longer than ten bytes");
    }

    private static long Saturate(long left, long right)
    {
        if (left <= 0 || right <= 0)
            return 0;

        return left > long.MaxValue / right ? long.MaxValue : left * right;
    }

    private static long AddSaturating(long left, long right)
    {
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }

    #endregion
}
