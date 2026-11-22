namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Reads the shape of a module a task brought (the raw WASI runtime) or that the compiler built
/// from a task's sources, before the engine compiles it. Compiling is work on the node that no
/// fuel meters and nothing can stop, and for some shapes it is far from linear in the module's
/// size. So the shape is bounded and priced before anything is compiled: a module past a bound
/// is refused with fixed words, the price is taken from the task's fuel budget, and a task that
/// cannot pay it is out of fuel without the module ever being compiled. The scan itself is one
/// pass over the code.
/// <para>
/// What the engine's compiler was measured to grow with (wasmtime 48.0.8, one thread), and what
/// is done about each:
/// <list type="bullet">
/// <item>the square of a function's locals times the depth of its loops (6.9 KB took 1.5 s, 28 KB
/// took 81 s and 1.5 GiB), and a branch table's targets times the blocks around it: priced;</item>
/// <item>the values a call or a block passes: 16 values through 80,000 calls took 29 s and
/// 1.6 GiB, 1,000 results through 2,000 nested blocks 8 GiB. A function type has at most
/// <see cref="MAX_PARAMETERS"/> parameters and <see cref="MAX_RESULTS"/> results, a block passes
/// at most <see cref="MAX_BLOCK_VALUES"/> values, and a block of two values or more is priced by
/// its values times the blocks around it;</item>
/// <item>the square of a function's branches (32,000 took 6.6 s, 128,000 took 76 s and 1 GiB),
/// and its locals times its branches (2,000 locals live across 20,000 branches took 31 s):
/// priced past what an ordinary function has;</item>
/// <item>a function, whatever is in it (80 microseconds and 6 KB while the module is compiled:
/// 400,000 empty ones took 33 s and 2.2 GiB), and a declared local (65 nanoseconds: 20,000
/// functions of 50,000 locals took 67 s): a function is priced as
/// <see cref="FUNCTION_FLOOR_BYTES"/> bytes at the least, a local past
/// <see cref="FREE_LOCALS"/> is priced, and both have a bound;</item>
/// <item>the size of one function: bounded; and the memory of a compile, which grows with the
/// function at hand and with what the compiler keeps of every function until the module is done
/// (64 functions of 65,000 calls that pass eight values took 1.9 GiB): estimated by the kinds of
/// the instructions (<see cref="VerifyCompileMemory"/>), and a module estimated over what a node
/// sets aside for one compile is refused.</item>
/// </list>
/// </para>
/// <para>
/// The prices were fitted to the engine as measured: the modules the controller ships compile at
/// 0.6 to 0.8 MiB a second, which a byte's price covers at the reference rate, and each shape's
/// price is its measured time in the same proportion. The bounds and the prices hold for the
/// shapes that were measured; they are not a proof about every module the engine accepts.
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
    /// What one unit of a function's branches costs, in fuel: (its branches)² units, for a
    /// function of more than <see cref="FREE_BRANCHES"/> branches.
    /// </summary>
    public const long COMPILE_FUEL_PER_BRANCH_UNIT = 4;

    /// <summary>The branches a function has before their square is priced: its bytes pay for them.</summary>
    public const int FREE_BRANCHES = 1024;

    /// <summary>
    /// What one unit of locals live across branches costs, in fuel: (the function's locals in
    /// use) × (its branches) units, past <see cref="FREE_LIVE_UNITS"/> a function.
    /// </summary>
    public const long COMPILE_FUEL_PER_LIVE_UNIT = 128;

    /// <summary>The units of locals times branches a function has before they are priced.</summary>
    public const long FREE_LIVE_UNITS = 16_384;

    /// <summary>
    /// What one unit of a block that passes values costs, in fuel: (its values) × (the blocks it
    /// sits in) units, for a block of two values or more.
    /// </summary>
    public const long COMPILE_FUEL_PER_BLOCK_UNIT = 4;

    /// <summary>What a declared local costs, in fuel, past <see cref="FREE_LOCALS"/> a function.</summary>
    public const long COMPILE_FUEL_PER_LOCAL = 64;

    /// <summary>The locals a function has before each one more is priced.</summary>
    public const int FREE_LOCALS = 256;

    /// <summary>The least a function is priced as, in bytes of its body: the engine's own work for any function.</summary>
    public const int FUNCTION_FLOOR_BYTES = 24;

    /// <summary>
    /// The deepest blocks, loops and ifs may nest in one function. The interpreters the controller
    /// ships nest 115 (Lua), 316 (QuickJS), 616 (Ruby) and 781 (CPython) deep; the limit is two and
    /// a half times the deepest of them. Past it a branch table over the nest costs the engine's
    /// compiler time with the cube of the depth (20,000 levels: 82 s and 2 GiB).
    /// </summary>
    public const int MAX_NESTING = 2048;

    /// <summary>
    /// The most parameters a function type of the module may have: the interpreters the controller
    /// ships and its compiler have 28 at the most. Parameters cost what the bytes that push them
    /// cost, so the bound is wide.
    /// </summary>
    public const int MAX_PARAMETERS = 128;

    /// <summary>
    /// The most results a function type of the module may have. A compiler for C, C++ or Rust
    /// gives a function one; past eight the cost of a call that passes them on grows several
    /// times with every doubling.
    /// </summary>
    public const int MAX_RESULTS = 8;

    /// <summary>The most values a block, a loop or an if may take and give together.</summary>
    public const int MAX_BLOCK_VALUES = 8;

    /// <summary>
    /// The most locals one function may have, its parameters included: the interpreters the
    /// controller ships and its compiler have 188 at the most.
    /// </summary>
    public const int MAX_LOCALS = 16_384;

    /// <summary>The most locals the functions of a module may declare together.</summary>
    public const long MAX_MODULE_LOCALS = 4_194_304;

    /// <summary>The most functions a module may define: CPython has 18,104, Ruby 17,746, QuickJS 1,522.</summary>
    public const int MAX_FUNCTIONS = 32_768;

    /// <summary>
    /// The most function types a module may have. The engine compiles code for every one, whatever
    /// uses it (600,000 of them, in 7 MB, took 3.8 GiB); the compiler the controller ships has 542,
    /// CPython 176, QuickJS 113.
    /// </summary>
    public const int MAX_TYPES = 4096;

    /// <summary>The most imports a module may have: CPython has 42, the compiler 29, a C program of the tests 9.</summary>
    public const int MAX_IMPORTS = 1024;

    /// <summary>
    /// The most exports a module may have: a program built to export all its functions has one a
    /// function and a few besides; the runtimes have 2 to 9.
    /// </summary>
    public const int MAX_EXPORTS = MAX_FUNCTIONS + 1024;

    /// <summary>
    /// The largest body one function may have, bytes. The largest of QuickJS is 78 KB and of Lua
    /// 31 KB; CPython's and Ruby's own (163 KB, 138 KB) belong to modules several times the size a
    /// task's module may have.
    /// </summary>
    public const int MAX_BODY_BYTES = 128 << 10;

    private const int SECTION_TYPE = 1;
    private const int SECTION_IMPORT = 2;
    private const int SECTION_FUNCTION = 3;
    private const int SECTION_GLOBAL = 6;
    private const int SECTION_EXPORT = 7;
    private const int SECTION_START = 8;
    private const int SECTION_ELEMENT = 9;
    private const int SECTION_CODE = 10;
    private const int HEADER_LENGTH = 8;

    private const byte FORM_FUNCTION = 0x60;

    private const byte OP_BLOCK = 0x02;
    private const byte OP_LOOP = 0x03;
    private const byte OP_IF = 0x04;
    private const byte OP_END = 0x0B;
    private const byte OP_BR = 0x0C;
    private const byte OP_BR_IF = 0x0D;
    private const byte OP_BR_TABLE = 0x0E;
    private const byte OP_CALL = 0x10;
    private const byte OP_CALL_INDIRECT = 0x11;
    private const byte OP_RETURN_CALL = 0x12;
    private const byte OP_RETURN_CALL_INDIRECT = 0x13;
    private const byte OP_REF_FUNC = 0xD2;

    private const long BLOCK_TYPE_EMPTY = -64;

    private const byte KIND_BLOCK = 0;
    private const byte KIND_LOOP = 1;

    private const string UNKNOWN_INSTRUCTION = "the module uses an instruction the sandbox does not take";
    private const string UNREADABLE = "the module's code cannot be read";

    /// <summary>Why a module whose compile may take more memory than a node sets aside for one is refused.</summary>
    public static readonly string TOO_MUCH_MEMORY = $"compiling the module may take more than {VerifyCompileMemory.MAX_BYTES >> 20} MiB of memory";

    /// <summary>Why a module with a start function is refused.</summary>
    public const string START_FUNCTION = "the module has a start function, which a task's module may not have: a program starts at _start";

    #endregion

    #region Fields

    private static readonly VerifyModuleShapeBounds TASK = new(MAX_NESTING, MAX_PARAMETERS, MAX_RESULTS, MAX_BLOCK_VALUES, MAX_LOCALS, MAX_MODULE_LOCALS, MAX_FUNCTIONS, MAX_BODY_BYTES, false,
        VerifyCompileMemory.MAX_BYTES, MAX_TYPES, MAX_IMPORTS, MAX_EXPORTS);
    private static readonly VerifyModuleShapeBounds NONE = new(1 << 20, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, long.MaxValue, int.MaxValue, int.MaxValue, true,
        long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue);

    #endregion

    #region Functions

    /// <summary>
    /// Prices a module and holds it to the bounds of a task's module.
    /// </summary>
    /// <param name="module">The module's bytes, already validated by the engine.</param>
    /// <returns>What compiling it costs in fuel, or why it is not compiled at all (fixed words).</returns>
    public static VerifyModuleShapeReport Scan(ReadOnlySpan<byte> module)
    {
        return Scan(module, TASK);
    }

    /// <summary>
    /// Prices a module without any bound but the deepest nesting the scan can follow: for
    /// measuring what modules are like.
    /// </summary>
    /// <param name="module">The module's bytes, already validated by the engine.</param>
    /// <returns>The price and what the module's shape is.</returns>
    public static VerifyModuleShapeReport Measure(ReadOnlySpan<byte> module)
    {
        return Scan(module, NONE);
    }

    private static VerifyModuleShapeReport Scan(ReadOnlySpan<byte> module, VerifyModuleShapeBounds bounds)
    {
        var bytes = Saturate(module.Length, COMPILE_FUEL_PER_BYTE);
        var tally = new VerifyModuleShapeTally { Cost = bytes };
        if (module.Length < HEADER_LENGTH)
            return new VerifyModuleShapeReport(tally.Cost, UNREADABLE);

        var position = HEADER_LENGTH;
        string? problem = null;

        try
        {
            while (position < module.Length && problem == null)
            {
                var id = module[position++];
                var size = (int)VerifyModuleCode.ReadU32(module, ref position);
                if (size < 0 || size > module.Length - position)
                    return new VerifyModuleShapeReport(tally.Cost, UNREADABLE);

                var section = module.Slice(position, size);
                position += size;

                problem = id switch
                {
                    SECTION_TYPE => ReadTypes(section, tally, bounds),
                    SECTION_IMPORT => Imports(section, tally, bounds),
                    SECTION_GLOBAL => Globals(section, tally),
                    SECTION_EXPORT => Exports(section, tally, bounds),
                    SECTION_ELEMENT => VerifyModuleSections.ReadElements(section, tally.Escaping) ? null : UNKNOWN_INSTRUCTION,
                    SECTION_FUNCTION => ReadFunctionTypes(section, tally, bounds),
                    SECTION_START => Start(tally, bounds),
                    SECTION_CODE => ScanCode(section, tally, bounds),
                    _ => null
                };
            }
        }
        catch (IndexOutOfRangeException)
        {
            return new VerifyModuleShapeReport(tally.Cost, UNREADABLE);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new VerifyModuleShapeReport(tally.Cost, UNREADABLE);
        }

        // The memory the compile may take: a module that may take more than a node sets aside for
        // one compile is refused, so whatever is compiled fits the gate.
        var surface = VerifyCompileMemory.Surface(tally.Types.Count, tally.TypeValues, tally.Imports, tally.Exports, tally.EscapingFunctions(out var escapingValues), escapingValues);
        var memory = VerifyCompileMemory.Estimate(module.Length, tally.FunctionTypes.Count, tally.LargestWork, tally.MostBlockUnits, tally.KeptUnits, surface);
        if (problem == null && memory > bounds.CompileMemory)
            problem = TOO_MUCH_MEMORY;

        return new VerifyModuleShapeReport(tally.Cost, problem)
        {
            DeepestNesting = tally.DeepestNesting,
            ShapeFuel = tally.Cost - bytes,
            Functions = tally.FunctionTypes.Count,
            WidestParameters = tally.WidestParameters,
            WidestResults = tally.WidestResults,
            WidestBlock = tally.WidestBlock,
            MostLocals = tally.MostLocals,
            Locals = tally.Locals,
            LargestBody = tally.LargestBody,
            MostBranches = tally.MostBranches,
            HasStartFunction = tally.HasStartFunction,
            KeptUnits = tally.KeptUnits,
            LargestWork = tally.LargestWork,
            SurfaceBytes = surface,
            CompileMemoryBytes = memory
        };
    }

    private static string? Start(VerifyModuleShapeTally tally, VerifyModuleShapeBounds bounds)
    {
        tally.HasStartFunction = true;
        return bounds.StartAllowed ? null : START_FUNCTION;
    }

    private static string? ReadTypes(ReadOnlySpan<byte> section, VerifyModuleShapeTally tally, VerifyModuleShapeBounds bounds)
    {
        var position = 0;
        var count = VerifyModuleSections.Count(section, ref position);
        if (count > bounds.Types)
            return $"the module has more than {bounds.Types} function types";

        for (var index = 0L; index < count; index++)
        {
            // Function types only: the engine is configured without the proposals that add others.
            if (section[position++] != FORM_FUNCTION)
                return UNKNOWN_INSTRUCTION;

            var parameters = VerifyModuleSections.Length(section, ref position);
            position += parameters;

            var results = VerifyModuleSections.Length(section, ref position);
            position += results;

            if (parameters > bounds.Parameters)
                return $"a function type of the module has more than {bounds.Parameters} parameters";

            if (results > bounds.Results)
                return $"a function type of the module has more than {bounds.Results} results";

            tally.TypeValues += parameters + (long)results;
            tally.WidestParameters = Math.Max(tally.WidestParameters, parameters);
            tally.WidestResults = Math.Max(tally.WidestResults, results);
            tally.Types.Add((parameters, results));
        }

        return null;
    }

    private static string? Imports(ReadOnlySpan<byte> section, VerifyModuleShapeTally tally, VerifyModuleShapeBounds bounds)
    {
        if (!VerifyModuleSections.ReadImports(section, tally.ImportTypes, out var imports))
            return UNKNOWN_INSTRUCTION;

        tally.Imports = imports;
        return imports > bounds.Imports ? $"the module has more than {bounds.Imports} imports" : null;
    }

    private static string? Exports(ReadOnlySpan<byte> section, VerifyModuleShapeTally tally, VerifyModuleShapeBounds bounds)
    {
        tally.Exports = VerifyModuleSections.ReadExports(section, tally.Escaping);
        return tally.Exports > bounds.Exports ? $"the module has more than {bounds.Exports} exports" : null;
    }

    private static string? Globals(ReadOnlySpan<byte> section, VerifyModuleShapeTally tally)
    {
        VerifyModuleSections.ReadGlobals(section, tally.Escaping);
        return null;
    }

    private static string? ReadFunctionTypes(ReadOnlySpan<byte> section, VerifyModuleShapeTally tally, VerifyModuleShapeBounds bounds)
    {
        var position = 0;
        var count = VerifyModuleCode.ReadU32(section, ref position);
        if (count > (uint)bounds.Functions)
            return $"the module has more than {bounds.Functions} functions";

        for (var index = 0u; index < count; index++)
            tally.FunctionTypes.Add((int)VerifyModuleCode.ReadU32(section, ref position));

        return null;
    }

    private static string? ScanCode(ReadOnlySpan<byte> section, VerifyModuleShapeTally tally, VerifyModuleShapeBounds bounds)
    {
        var position = 0;
        var count = VerifyModuleCode.ReadU32(section, ref position);
        if (count > (uint)bounds.Functions)
            return $"the module has more than {bounds.Functions} functions";

        var kinds = new byte[bounds.Nesting + 2];

        for (var index = 0; index < count; index++)
        {
            var size = VerifyModuleSections.Length(section, ref position);
            if (size > bounds.BodyBytes)
                return $"a function of the module is larger than {bounds.BodyBytes >> 10} KiB";

            var body = section.Slice(position, size);
            position += size;

            // A function is the engine's work whatever is in it: the smallest are priced as a few bytes more.
            tally.Cost = AddSaturating(tally.Cost, Saturate(Math.Max(0, FUNCTION_FLOOR_BYTES - size), COMPILE_FUEL_PER_BYTE));
            tally.LargestBody = Math.Max(tally.LargestBody, size);

            var type = index < tally.FunctionTypes.Count ? tally.FunctionTypes[index] : -1;
            var parameters = type >= 0 && type < tally.Types.Count ? tally.Types[type].Parameters : 0;
            var problem = ScanFunction(body, parameters, kinds, tally, bounds);
            if (problem != null)
                return problem;

            tally.LargestWork = Math.Max(tally.LargestWork, VerifyCompileMemory.Work(size, tally.FunctionWork));
        }

        return null;
    }

    private static string? ScanFunction(ReadOnlySpan<byte> body, int parameters, byte[] kinds, VerifyModuleShapeTally tally, VerifyModuleShapeBounds bounds)
    {
        var position = 0;

        long locals = parameters;
        long declared = 0;
        var groups = VerifyModuleCode.ReadU32(body, ref position);
        for (var group = 0u; group < groups; group++)
        {
            declared += VerifyModuleCode.ReadU32(body, ref position);
            position++; // the group's value type: one byte without the proposals that widen it
        }

        locals += declared;
        if (locals > bounds.Locals)
            return $"a function of the module has more than {bounds.Locals} locals";

        tally.Locals += declared;
        if (tally.Locals > bounds.ModuleLocals)
            return $"the functions of the module declare more than {bounds.ModuleLocals} locals together";

        tally.MostLocals = (int)Math.Max(tally.MostLocals, locals);

        tally.FunctionWork = 0;

        var depth = 1;
        var loops = 0;
        var deepestLoops = 0;
        long localUses = 0;
        long widestLoop = 0;
        long tableUnits = 0;
        long blockUnits = 0;
        long branches = 0;
        kinds[0] = KIND_BLOCK;

        // Every instruction is a byte at least: a scan that reads more instructions than bytes has gone wrong.
        var steps = 0;
        while (depth > 0)
        {
            if (++steps > body.Length)
                return UNREADABLE;

            var op = body[position++];
            if (op is not (OP_BR_TABLE or OP_CALL or OP_CALL_INDIRECT or OP_RETURN_CALL or OP_RETURN_CALL_INDIRECT))
            {
                // What the compiler keeps of the instruction, and what it takes while it works on it.
                var sub = VerifyCompileMemory.SubOf(op, body, position);
                tally.KeptUnits = AddSaturating(tally.KeptUnits, VerifyCompileMemory.Units(op, sub));
                tally.FunctionWork = AddSaturating(tally.FunctionWork, VerifyCompileMemory.WorkOf(op, sub));
            }

            switch (op)
            {
                case OP_BLOCK:
                case OP_LOOP:
                case OP_IF:
                    var blockType = VerifyModuleCode.ReadS64(body, ref position);
                    if (depth > bounds.Nesting)
                        return $"a function of the module nests blocks deeper than {bounds.Nesting}";

                    tally.DeepestNesting = Math.Max(tally.DeepestNesting, depth);

                    // What the block takes and gives: nothing, one value, or a function type's.
                    long taken = 0;
                    long values = blockType == BLOCK_TYPE_EMPTY ? 0 : 1;
                    if (blockType >= 0 && blockType < tally.Types.Count)
                    {
                        taken = tally.Types[(int)blockType].Parameters;
                        values = taken + tally.Types[(int)blockType].Results;
                    }

                    if (values > bounds.BlockValues)
                        return $"a block of the module takes and gives more than {bounds.BlockValues} values";

                    tally.WidestBlock = (int)Math.Max(tally.WidestBlock, values);
                    if (values > 1)
                        blockUnits = AddSaturating(blockUnits, Saturate(values, depth));

                    if (op == OP_LOOP)
                    {
                        loops++;
                        deepestLoops = Math.Max(deepestLoops, loops);

                        // A loop that takes values takes them as the engine's compiler takes locals.
                        widestLoop = Math.Max(widestLoop, taken);
                    }

                    if (op == OP_IF)
                        branches++;

                    kinds[depth++] = op == OP_LOOP ? KIND_LOOP : KIND_BLOCK;
                    break;
                case OP_END:
                    if (kinds[--depth] == KIND_LOOP)
                        loops--;
                    break;
                case OP_BR:
                case OP_BR_IF:
                    VerifyModuleCode.ReadU32(body, ref position);
                    branches++;
                    break;
                case OP_BR_TABLE:
                    var targets = VerifyModuleCode.ReadU32(body, ref position);
                    for (var target = 0u; target <= targets; target++)
                        VerifyModuleCode.ReadU32(body, ref position);
                    tableUnits = AddSaturating(tableUnits, Saturate(targets + 1, depth));
                    branches += targets + 1;
                    tally.KeptUnits = AddSaturating(tally.KeptUnits, Saturate(targets + 1, VerifyCompileMemory.UNITS_BRANCH));
                    break;
                case OP_CALL:
                case OP_RETURN_CALL:
                    var called = tally.TypeOfFunction(VerifyModuleCode.ReadU32(body, ref position));
                    tally.KeptUnits = AddSaturating(tally.KeptUnits, tally.CallUnits(false, called));
                    break;
                case OP_REF_FUNC:
                    tally.Escaping.Add(VerifyModuleCode.ReadU32(body, ref position));
                    break;
                case OP_CALL_INDIRECT:
                case OP_RETURN_CALL_INDIRECT:
                    var typeIndex = (int)Math.Min(int.MaxValue, VerifyModuleCode.ReadU32(body, ref position));
                    VerifyModuleCode.ReadU32(body, ref position);
                    tally.KeptUnits = AddSaturating(tally.KeptUnits, tally.CallUnits(true, typeIndex));
                    tally.FunctionWork = AddSaturating(tally.FunctionWork, VerifyCompileMemory.WORK_INDIRECT_CALL);
                    break;
                case 0x20:
                case 0x21:
                case 0x22:
                    VerifyModuleCode.ReadU32(body, ref position);
                    localUses++;
                    break;
                default:
                    if (!VerifyModuleCode.SkipImmediates(op, body, ref position))
                        return UNKNOWN_INSTRUCTION;
                    break;
            }
        }

        if (position != body.Length)
            return UNREADABLE;

        var live = Math.Min(locals, localUses) + widestLoop;
        var loopUnits = Saturate(Saturate(live, live), deepestLoops);
        var liveUnits = Math.Max(0, Saturate(live, branches) - FREE_LIVE_UNITS);
        var branchUnits = branches > FREE_BRANCHES ? Saturate(branches, branches) : 0;

        tally.MostBranches = Math.Max(tally.MostBranches, branches);
        tally.MostBlockUnits = Math.Max(tally.MostBlockUnits, blockUnits);

        tally.Cost = AddSaturating(tally.Cost, Saturate(loopUnits, COMPILE_FUEL_PER_LOOP_UNIT));
        tally.Cost = AddSaturating(tally.Cost, Saturate(tableUnits, COMPILE_FUEL_PER_TABLE_UNIT));
        tally.Cost = AddSaturating(tally.Cost, Saturate(branchUnits, COMPILE_FUEL_PER_BRANCH_UNIT));
        tally.Cost = AddSaturating(tally.Cost, Saturate(liveUnits, COMPILE_FUEL_PER_LIVE_UNIT));
        tally.Cost = AddSaturating(tally.Cost, Saturate(blockUnits, COMPILE_FUEL_PER_BLOCK_UNIT));
        tally.Cost = AddSaturating(tally.Cost, Saturate(Math.Max(0, locals - FREE_LOCALS), COMPILE_FUEL_PER_LOCAL));
        return null;
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
