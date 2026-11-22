using System.Text;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// Modules written byte by byte, for the shapes whose compile cost the audit of 2026-11-18
/// measured (finding N-2) and that the text format cannot hold in a reasonable text: wide calls,
/// values carried by branches, locals live across branches, declared locals, and results passed
/// through nested blocks. Each exports a memory and a <c>_start</c> that returns at once, so a
/// module that is compiled also runs.
/// </summary>
internal static class ShapeModules
{
    #region Constants

    private const byte I32 = 0x7F;
    private const byte I64 = 0x7E;

    private static readonly byte[] NOTHING = [0x00, 0x0B];

    #endregion

    #region Shapes

    /// <summary>
    /// Functions that push a number of values and pass them through a call of that many
    /// parameters and results a number of times.
    /// </summary>
    /// <param name="values">The parameters, and the results, of the function called.</param>
    /// <param name="calls">The calls in one function.</param>
    /// <param name="functions">The functions of that shape.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Calls(int values, int calls, int functions = 1)
    {
        return CallsOf(values, values, calls, functions);
    }

    /// <summary>
    /// Functions that call a function of a number of parameters and of results a number of
    /// times, each call taking what the one before it returned (and constants, or drops, for the
    /// difference).
    /// </summary>
    /// <param name="parameters">The parameters of the function called.</param>
    /// <param name="results">Its results.</param>
    /// <param name="calls">The calls in one function.</param>
    /// <param name="functions">The functions of that shape.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] CallsOf(int parameters, int results, int calls, int functions = 1)
    {
        var heavy = new List<byte> { 0x00 };
        for (var i = 0; i < parameters; i++)
            heavy.AddRange([0x42, 0x00]);

        for (var i = 0; i < calls; i++)
        {
            heavy.AddRange([0x10, 0x01]);
            if (i == calls - 1)
                break;

            for (var more = results; more < parameters; more++)
                heavy.AddRange([0x42, 0x00]);

            for (var less = parameters; less < results; less++)
                heavy.Add(0x1A);
        }

        for (var i = 0; i < results; i++)
            heavy.Add(0x1A);

        heavy.Add(0x0B);

        // The function called never returns: what is measured is the calls, not their target.
        var bodies = new List<(int Type, byte[] Body)> { (0, NOTHING), (1, [0x00, 0x00, 0x0B]) };
        for (var i = 0; i < functions; i++)
            bodies.Add((0, heavy.ToArray()));

        return Build([([], []), (Enumerable.Repeat(I64, parameters).ToArray(), Enumerable.Repeat(I64, results).ToArray())], bodies, 1);
    }

    /// <summary>
    /// One function of a number of blocks one after another, each with one conditional branch
    /// out of it: as many branches as <see cref="Branches"/> has, none of them to the same place.
    /// </summary>
    /// <param name="blocks">The blocks.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Chain(int blocks)
    {
        var body = new List<byte> { 0x00 };
        for (var i = 0; i < blocks; i++)
            body.AddRange([0x02, 0x40, 0x20, 0x00, 0x0D, 0x00, 0x0B]);

        body.Add(0x0B);
        return Build([([], []), ([I32], [])], [(0, NOTHING), (1, body.ToArray())], 1);
    }

    /// <summary>
    /// Functions with a number of values on the stack of a block and a number of conditional
    /// branches out of it, each of which carries the values.
    /// </summary>
    /// <param name="values">The results of the block.</param>
    /// <param name="branches">The branches out of it.</param>
    /// <param name="functions">The functions of that shape.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Branches(int values, int branches, int functions = 1)
    {
        var wide = Enumerable.Repeat(I64, values).ToArray();
        var heavy = new List<byte> { 0x00, 0x02 };
        heavy.AddRange(Sleb(1));
        for (var i = 0; i < values; i++)
            heavy.AddRange([0x42, 0x00]);

        for (var i = 0; i < branches; i++)
            heavy.AddRange([0x20, 0x00, 0x0D, 0x00]);

        heavy.Add(0x0B);
        for (var i = 0; i < values; i++)
            heavy.Add(0x1A);

        heavy.Add(0x0B);

        var bodies = new List<(int Type, byte[] Body)> { (0, NOTHING) };
        for (var i = 0; i < functions; i++)
            bodies.Add((2, heavy.ToArray()));

        return Build([([], []), ([], wide), ([I32], [])], bodies, 1);
    }

    /// <summary>
    /// Functions that read a number of values from the memory into locals, branch out of a block
    /// a number of times, and then use every value.
    /// </summary>
    /// <param name="values">The locals that are live across the branches.</param>
    /// <param name="branches">The conditional branches out of the block.</param>
    /// <param name="functions">The functions of that shape.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Live(int values, int branches, int functions = 1)
    {
        var heavy = new List<byte> { 0x01 };
        heavy.AddRange(Leb((ulong)values));
        heavy.Add(I64);

        for (var i = 1; i <= values; i++)
        {
            heavy.Add(0x41);
            heavy.AddRange(Sleb(8L * i));
            heavy.AddRange([0x29, 0x03, 0x00, 0x21]);
            heavy.AddRange(Leb((ulong)i));
        }

        heavy.AddRange([0x02, 0x40]);
        for (var i = 0; i < branches; i++)
            heavy.AddRange([0x20, 0x00, 0x0D, 0x00]);

        heavy.Add(0x0B);

        heavy.AddRange([0x41, 0x00, 0x20, 0x01]);
        for (var i = 2; i <= values; i++)
        {
            heavy.Add(0x20);
            heavy.AddRange(Leb((ulong)i));
            heavy.Add(0x85);
        }

        heavy.AddRange([0x37, 0x03, 0x00, 0x0B]);

        var bodies = new List<(int Type, byte[] Body)> { (0, NOTHING) };
        for (var i = 0; i < functions; i++)
            bodies.Add((1, heavy.ToArray()));

        var pages = (int)(8L * (values + 1) / 65536) + 1;
        return Build([([], []), ([I32], [])], bodies, pages);
    }

    /// <summary>A number of functions that declare a number of locals and do nothing.</summary>
    /// <param name="functions">The functions.</param>
    /// <param name="locals">The locals each declares, in one group.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Locals(int functions, int locals)
    {
        var body = new List<byte>();
        if (locals > 0)
        {
            body.Add(0x01);
            body.AddRange(Leb((ulong)locals));
            body.Add(I64);
        }
        else
        {
            body.Add(0x00);
        }

        body.Add(0x0B);

        var bodies = new List<(int Type, byte[] Body)> { (0, NOTHING) };
        var one = body.ToArray();
        for (var i = 0; i < functions; i++)
            bodies.Add((0, one));

        return Build([([], [])], bodies, 1);
    }

    /// <summary>Functions of blocks nested a number of levels deep, each with a number of results.</summary>
    /// <param name="results">The results of every block.</param>
    /// <param name="depth">How deep the blocks nest.</param>
    /// <param name="functions">The functions of that shape.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Blocks(int results, int depth, int functions = 1)
    {
        var wide = Enumerable.Repeat(I64, results).ToArray();
        var heavy = new List<byte> { 0x00 };
        for (var i = 0; i < depth; i++)
        {
            heavy.Add(0x02);
            heavy.AddRange(Sleb(1));
        }

        for (var i = 0; i < results; i++)
            heavy.AddRange([0x42, 0x00]);

        for (var i = 0; i < depth; i++)
            heavy.Add(0x0B);

        for (var i = 0; i < results; i++)
            heavy.Add(0x1A);

        heavy.Add(0x0B);

        var bodies = new List<(int Type, byte[] Body)> { (0, NOTHING) };
        for (var i = 0; i < functions; i++)
            bodies.Add((0, heavy.ToArray()));

        return Build([([], []), ([], wide)], bodies, 1);
    }

    /// <summary>A module whose one function type has a number of parameters and of results, used by nothing.</summary>
    /// <param name="parameters">The parameters of the type.</param>
    /// <param name="results">The results of the type.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] WideType(int parameters, int results)
    {
        return Build([([], []), (Enumerable.Repeat(I64, parameters).ToArray(), Enumerable.Repeat(I64, results).ToArray())], [(0, NOTHING)], 1);
    }

    /// <summary>A module whose one function is a body of a size: nothing but instructions that do nothing.</summary>
    /// <param name="bytes">The size of the body, its locals and its end included.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Body(int bytes)
    {
        var body = new byte[bytes];
        Array.Fill(body, (byte)0x01);
        body[0] = 0x00;
        body[^1] = 0x0B;
        return Build([([], [])], [(0, NOTHING), (0, body)], 1);
    }

    #endregion

    #region Tools

    /// <summary>
    /// Puts a module together: its types, its functions (the first is exported as <c>_start</c>),
    /// one memory exported as <c>memory</c>.
    /// </summary>
    /// <param name="types">The function types: parameters and results.</param>
    /// <param name="functions">The functions: the index of the type and the body, locals included.</param>
    /// <param name="pages">The pages of the memory.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Build(IReadOnlyList<(byte[] Parameters, byte[] Results)> types, IReadOnlyList<(int Type, byte[] Body)> functions, int pages)
    {
        return Build(types, [], functions, pages, 0);
    }

    /// <summary>
    /// Puts a module together: its types, the functions it imports (from <c>env</c>, as <c>f0</c>,
    /// <c>f1</c>...), its own functions (the first of them is exported as <c>_start</c>), a table of
    /// function references when asked for, one memory exported as <c>memory</c>.
    /// </summary>
    /// <param name="types">The function types: parameters and results.</param>
    /// <param name="imports">The type of every imported function; they come first in the function index space.</param>
    /// <param name="functions">The functions: the index of the type and the body, locals included.</param>
    /// <param name="pages">The pages of the memory.</param>
    /// <param name="tableElements">The elements of a table of function references, or 0 for no table.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Build(IReadOnlyList<(byte[] Parameters, byte[] Results)> types, IReadOnlyList<int> imports,
        IReadOnlyList<(int Type, byte[] Body)> functions, int pages, int tableElements)
    {
        var module = new List<byte> { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 };

        var typeSection = new List<byte>(Leb((ulong)types.Count));
        foreach (var (parameters, results) in types)
        {
            typeSection.Add(0x60);
            typeSection.AddRange(Leb((ulong)parameters.Length));
            typeSection.AddRange(parameters);
            typeSection.AddRange(Leb((ulong)results.Length));
            typeSection.AddRange(results);
        }

        Section(module, 1, typeSection);

        if (imports.Count > 0)
        {
            var importSection = new List<byte>(Leb((ulong)imports.Count));
            for (var i = 0; i < imports.Count; i++)
            {
                importSection.AddRange(Name("env"));
                importSection.AddRange(Name($"f{i}"));
                importSection.Add(0x00);
                importSection.AddRange(Leb((ulong)imports[i]));
            }

            Section(module, 2, importSection);
        }

        var functionSection = new List<byte>(Leb((ulong)functions.Count));
        foreach (var (type, _) in functions)
            functionSection.AddRange(Leb((ulong)type));

        Section(module, 3, functionSection);

        if (tableElements > 0)
        {
            var tableSection = new List<byte> { 0x01, 0x70, 0x00 };
            tableSection.AddRange(Leb((ulong)tableElements));
            Section(module, 4, tableSection);
        }

        var memorySection = new List<byte> { 0x01, 0x00 };
        memorySection.AddRange(Leb((ulong)pages));
        Section(module, 5, memorySection);

        var exportSection = new List<byte> { 0x02 };
        exportSection.AddRange(Name("memory"));
        exportSection.AddRange([0x02, 0x00]);
        exportSection.AddRange(Name("_start"));
        exportSection.Add(0x00);
        exportSection.AddRange(Leb((ulong)imports.Count));
        Section(module, 7, exportSection);

        var codeSection = new List<byte>(Leb((ulong)functions.Count));
        foreach (var (_, body) in functions)
        {
            codeSection.AddRange(Leb((ulong)body.Length));
            codeSection.AddRange(body);
        }

        Section(module, 10, codeSection);
        return module.ToArray();
    }

    private static void Section(List<byte> module, byte id, List<byte> content)
    {
        module.Add(id);
        module.AddRange(Leb((ulong)content.Count));
        module.AddRange(content);
    }

    private static List<byte> Name(string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name);
        var encoded = new List<byte>(Leb((ulong)bytes.Length));
        encoded.AddRange(bytes);
        return encoded;
    }

    private static List<byte> Leb(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            var part = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
                part |= 0x80;

            bytes.Add(part);
        } while (value != 0);

        return bytes;
    }

    private static List<byte> Sleb(long value)
    {
        var bytes = new List<byte>();
        while (true)
        {
            var part = (byte)(value & 0x7F);
            value >>= 7;
            var done = (value == 0 && (part & 0x40) == 0) || (value == -1 && (part & 0x40) != 0);
            if (!done)
                part |= 0x80;

            bytes.Add(part);
            if (done)
                return bytes;
        }
    }

    #endregion
}
