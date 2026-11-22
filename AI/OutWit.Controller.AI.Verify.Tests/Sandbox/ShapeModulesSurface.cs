using System.Text;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// Modules written byte by byte for what the engine compiles besides the bodies of the functions:
/// code for every function type, for every imported function, and for every function that leaves
/// the module (exported, in a table, or named by <c>ref.func</c>). Each exports a memory and a
/// <c>_start</c> that returns at once.
/// </summary>
internal static class ShapeModulesSurface
{
    #region Constants

    private const byte I64 = 0x7E;

    private static readonly byte[] VALUE_TYPES = [0x7F, 0x7E, 0x7D, 0x7C];

    private static readonly byte[] NOTHING = [0x00, 0x0B];

    #endregion

    #region Shapes

    /// <summary>A module of a number of function types, all of them different, and one function: <c>_start</c>.</summary>
    /// <param name="types">The function types, the type of <c>_start</c> among them.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] ManyTypes(int types)
    {
        var list = new List<(byte[] Parameters, byte[] Results)> { ([], []) };
        for (var index = 1; list.Count < types; index++)
            list.Add((Parameters(index), []));

        return Compose(list, [], [(0, NOTHING)], [], [], 0);
    }

    /// <summary>
    /// A module of a number of function types of many parameters, all of them different (a 64-bit
    /// parameter in front of a different list of the four value types each), and one function.
    /// </summary>
    /// <param name="types">The function types, the type of <c>_start</c> among them.</param>
    /// <param name="parameters">The 64-bit parameters in front of every type but the first.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] WideTypes(int types, int parameters)
    {
        var list = new List<(byte[] Parameters, byte[] Results)> { ([], []) };
        for (var index = 1; list.Count < types; index++)
            list.Add(([.. Enumerable.Repeat(I64, parameters), .. Parameters(index)], []));

        return Compose(list, [], [(0, NOTHING)], [], [], 0);
    }

    /// <summary>Functions of a number of 64-bit parameters, with empty bodies, each exported under a name of its own.</summary>
    /// <param name="functions">The functions besides <c>_start</c>.</param>
    /// <param name="parameters">The parameters of each.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] ExportedFunctions(int functions, int parameters)
    {
        var bodies = new List<(int Type, byte[] Body)> { (0, NOTHING) };
        var exports = new List<(string Name, int Function)>();
        for (var i = 0; i < functions; i++)
        {
            bodies.Add((1, NOTHING));
            exports.Add(($"f{i}", 1 + i));
        }

        return Compose([([], []), (Enumerable.Repeat(I64, parameters).ToArray(), [])], [], bodies, exports, [], 0);
    }

    /// <summary>Functions of a number of 64-bit parameters, with empty bodies, all of them in a table (so they leave the module).</summary>
    /// <param name="functions">The functions besides <c>_start</c>.</param>
    /// <param name="parameters">The parameters of each.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] TabledFunctions(int functions, int parameters)
    {
        var bodies = new List<(int Type, byte[] Body)> { (0, NOTHING) };
        var elements = new List<int>();
        for (var i = 0; i < functions; i++)
        {
            bodies.Add((1, NOTHING));
            elements.Add(1 + i);
        }

        return Compose([([], []), (Enumerable.Repeat(I64, parameters).ToArray(), [])], [], bodies, [], elements, functions);
    }

    /// <summary>A number of imported functions, of a number of different types (taken in turn), and <c>_start</c>.</summary>
    /// <param name="imports">The imported functions.</param>
    /// <param name="types">The different types among them.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] ManyImports(int imports, int types)
    {
        var list = new List<(byte[] Parameters, byte[] Results)> { ([], []) };
        for (var index = 1; list.Count < types + 1; index++)
            list.Add((Parameters(index), []));

        var importTypes = new List<int>();
        for (var i = 0; i < imports; i++)
            importTypes.Add(1 + i % types);

        return Compose(list, importTypes, [(0, NOTHING)], [], [], 0);
    }

    /// <summary><c>_start</c>, exported besides under a number of other names.</summary>
    /// <param name="exports">The other names.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] ManyExports(int exports)
    {
        var list = new List<(string Name, int Function)>();
        for (var i = 0; i < exports; i++)
            list.Add(($"e{i}", 0));

        return Compose([([], [])], [], [(0, NOTHING)], list, [], 0);
    }

    #endregion

    #region Tools

    /// <summary>
    /// Puts a module together: its types, the functions it imports (from <c>e</c>, as <c>i0</c>,
    /// <c>i1</c>...), its own functions (the first is exported as <c>_start</c>), the exports given
    /// besides, a table of function references with the functions given in it (from index 0), one
    /// memory exported as <c>memory</c>.
    /// </summary>
    public static byte[] Compose(IReadOnlyList<(byte[] Parameters, byte[] Results)> types, IReadOnlyList<int> imports,
        IReadOnlyList<(int Type, byte[] Body)> functions, IReadOnlyList<(string Name, int Function)> exports, IReadOnlyList<int> elements, int tableElements)
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
                importSection.AddRange(Name("e"));
                importSection.AddRange(Name($"i{i}"));
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

        Section(module, 5, [0x01, 0x00, 0x01]);

        var exportSection = new List<byte>(Leb((ulong)(2 + exports.Count)));
        exportSection.AddRange(Name("memory"));
        exportSection.AddRange([0x02, 0x00]);
        exportSection.AddRange(Name("_start"));
        exportSection.Add(0x00);
        exportSection.AddRange(Leb((ulong)imports.Count));
        foreach (var (name, function) in exports)
        {
            exportSection.AddRange(Name(name));
            exportSection.Add(0x00);
            exportSection.AddRange(Leb((ulong)(imports.Count + function)));
        }

        Section(module, 7, exportSection);

        if (elements.Count > 0)
        {
            var elementSection = new List<byte> { 0x01, 0x00, 0x41, 0x00, 0x0B };
            elementSection.AddRange(Leb((ulong)elements.Count));
            foreach (var function in elements)
                elementSection.AddRange(Leb((ulong)(imports.Count + function)));

            Section(module, 9, elementSection);
        }

        var codeSection = new List<byte>(Leb((ulong)functions.Count));
        foreach (var (_, body) in functions)
        {
            codeSection.AddRange(Leb((ulong)body.Length));
            codeSection.AddRange(body);
        }

        Section(module, 10, codeSection);
        return module.ToArray();
    }

    /// <summary>The parameters of the index-th type after the empty one: every list of the four value types, shortest first.</summary>
    private static byte[] Parameters(int index)
    {
        var length = 1;
        var count = 4L;
        var rest = (long)index - 1;
        while (rest >= count)
        {
            rest -= count;
            length++;
            count *= 4;
        }

        var parameters = new byte[length];
        for (var i = 0; i < length; i++)
        {
            parameters[i] = VALUE_TYPES[rest % 4];
            rest /= 4;
        }

        return parameters;
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

    #endregion
}
