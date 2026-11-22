namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// Modules written byte by byte for what the engine's compiler keeps of every function until the
/// module is done and takes for the function at hand, by the kind of the instructions: calls of
/// the module's functions, of imports and through a table, loads, stores, arithmetic, divisions,
/// conversions that may trap and calls into the engine. Each shape is a number of functions of
/// the same body, so that two numbers of functions tell what one more keeps. Each module exports
/// a memory and a <c>_start</c> that returns at once (as <see cref="ShapeModules"/> builds them).
/// </summary>
internal static class ShapeModulesKept
{
    #region Constants

    private const byte I64 = 0x7E;

    private static readonly byte[] NOTHING = [0x00, 0x0B];

    #endregion

    #region Shapes

    /// <summary>
    /// Functions that call a function of a number of parameters and as many results through a
    /// table a number of times, each call taking what the one before it returned.
    /// </summary>
    /// <param name="values">The parameters, and the results, of the function called.</param>
    /// <param name="calls">The calls in one function.</param>
    /// <param name="functions">The functions of that shape.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] IndirectCalls(int values, int calls, int functions = 1)
    {
        var heavy = ChainOfCalls(values, calls, [0x41, 0x00, 0x11, 0x01, 0x00]);
        var bodies = new List<(int Type, byte[] Body)> { (0, NOTHING), (1, [0x00, 0x00, 0x0B]) };
        for (var i = 0; i < functions; i++)
            bodies.Add((0, heavy));

        return ShapeModules.Build([([], []), Wide(values)], [], bodies, 1, 1);
    }

    /// <summary>
    /// Functions that call an imported function of a number of parameters and as many results a
    /// number of times, each call taking what the one before it returned.
    /// </summary>
    /// <param name="values">The parameters, and the results, of the function called.</param>
    /// <param name="calls">The calls in one function.</param>
    /// <param name="functions">The functions of that shape.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] ImportedCalls(int values, int calls, int functions = 1)
    {
        var heavy = ChainOfCalls(values, calls, [0x10, 0x00]);
        var bodies = new List<(int Type, byte[] Body)> { (0, NOTHING) };
        for (var i = 0; i < functions; i++)
            bodies.Add((0, heavy));

        return ShapeModules.Build([([], []), Wide(values)], [1], bodies, 1, 0);
    }

    /// <summary>Functions of nothing but loads of a 64-bit value from the memory, each dropped.</summary>
    /// <param name="loads">The loads in one function.</param>
    /// <param name="functions">The functions of that shape.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Loads(int loads, int functions = 1)
    {
        return Repeated([0x00], [0x41, 0x00, 0x29, 0x03, 0x00, 0x1A], loads, functions);
    }

    /// <summary>
    /// Functions that add up a number of 64-bit values loaded from the memory, each at an address of
    /// its own from a base read from the memory, and store the sum: every load is used, so none can
    /// be left out.
    /// </summary>
    /// <param name="loads">The loads in one function.</param>
    /// <param name="functions">The functions of that shape.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] SummedLoads(int loads, int functions = 1)
    {
        var body = new List<byte> { 0x02, 0x01, 0x7F, 0x01, I64, 0x41, 0x00, 0x28, 0x02, 0x00, 0x21, 0x00 };
        for (var i = 0; i < loads; i++)
        {
            body.AddRange([0x20, 0x00, 0x29, 0x03]);
            body.AddRange(Leb((ulong)(8 * i % 65536)));
            body.AddRange([0x20, 0x01, 0x7C, 0x21, 0x01]);
        }

        body.AddRange([0x41, 0x00, 0x20, 0x01, 0x37, 0x03, 0x00, 0x0B]);

        var bodies = new List<(int Type, byte[] Body)> { (0, NOTHING) };
        var heavy = body.ToArray();
        for (var i = 0; i < functions; i++)
            bodies.Add((0, heavy));

        return ShapeModules.Build([([], [])], bodies, 2);
    }

    /// <summary>
    /// Functions that turn a 64-bit value read from the memory into a float and back a number of
    /// times, adding one each time: every conversion back may trap, and each depends on the one before.
    /// </summary>
    /// <param name="conversions">The conversions in one function.</param>
    /// <param name="functions">The functions of that shape.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Truncations(int conversions, int functions = 1)
    {
        var body = new List<byte> { 0x01, 0x01, I64, 0x41, 0x00, 0x29, 0x03, 0x00, 0x21, 0x00 };
        for (var i = 0; i < conversions; i++)
            body.AddRange([0x20, 0x00, 0xB9, 0xB0, 0x42, 0x01, 0x7C, 0x21, 0x00]);

        body.AddRange([0x41, 0x00, 0x20, 0x00, 0x37, 0x03, 0x00, 0x0B]);

        var bodies = new List<(int Type, byte[] Body)> { (0, NOTHING) };
        var heavy = body.ToArray();
        for (var i = 0; i < functions; i++)
            bodies.Add((0, heavy));

        return ShapeModules.Build([([], [])], bodies, 1);
    }

    /// <summary>
    /// Functions that multiply a 64-bit value read from the memory and add to it a number of times,
    /// each step depending on the one before, and store the result: arithmetic nothing can fold.
    /// </summary>
    /// <param name="steps">The steps (a multiplication and an addition each) in one function.</param>
    /// <param name="functions">The functions of that shape.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Arithmetic(int steps, int functions = 1)
    {
        var body = new List<byte> { 0x01, 0x01, I64, 0x41, 0x00, 0x29, 0x03, 0x00, 0x21, 0x00 };
        for (var i = 0; i < steps; i++)
            body.AddRange([0x20, 0x00, 0x42, 0x03, 0x7E, 0x42, 0x01, 0x7C, 0x21, 0x00]);

        body.AddRange([0x41, 0x00, 0x20, 0x00, 0x37, 0x03, 0x00, 0x0B]);

        var bodies = new List<(int Type, byte[] Body)> { (0, NOTHING) };
        var heavy = body.ToArray();
        for (var i = 0; i < functions; i++)
            bodies.Add((0, heavy));

        return ShapeModules.Build([([], [])], bodies, 1);
    }

    /// <summary>Functions of nothing but stores of a 64-bit value into the memory.</summary>
    /// <param name="stores">The stores in one function.</param>
    /// <param name="functions">The functions of that shape.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Stores(int stores, int functions = 1)
    {
        return Repeated([0x00], [0x41, 0x00, 0x42, 0x00, 0x37, 0x03, 0x00], stores, functions);
    }

    /// <summary>Functions of nothing but additions to a local.</summary>
    /// <param name="additions">The additions in one function.</param>
    /// <param name="functions">The functions of that shape.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Additions(int additions, int functions = 1)
    {
        return Repeated([0x01, 0x01, I64], [0x20, 0x00, 0x42, 0x01, 0x7C, 0x21, 0x00], additions, functions);
    }

    /// <summary>Functions of nothing but divisions of one local by another (both read from the memory first), each a place the engine may trap.</summary>
    /// <param name="divisions">The divisions in one function.</param>
    /// <param name="functions">The functions of that shape.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Divisions(int divisions, int functions = 1)
    {
        return Repeated([0x01, 0x02, I64, 0x41, 0x00, 0x29, 0x03, 0x00, 0x21, 0x00, 0x41, 0x08, 0x29, 0x03, 0x00, 0x21, 0x01], [0x20, 0x00, 0x20, 0x01, 0x7F, 0x21, 0x00], divisions, functions);
    }

    /// <summary>Functions of nothing but attempts to grow the memory, each a call into the engine.</summary>
    /// <param name="grows">The attempts in one function.</param>
    /// <param name="functions">The functions of that shape.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Grows(int grows, int functions = 1)
    {
        return Repeated([0x00], [0x41, 0x00, 0x40, 0x00, 0x1A], grows, functions);
    }

    #endregion

    #region Tools

    /// <summary>A function type of a number of 64-bit parameters and as many results.</summary>
    private static (byte[] Parameters, byte[] Results) Wide(int values)
    {
        return (Enumerable.Repeat(I64, values).ToArray(), Enumerable.Repeat(I64, values).ToArray());
    }

    /// <summary>
    /// A body that pushes a number of values, passes them through a call a number of times (the
    /// instruction bytes given, which leave as many values as they take) and drops them.
    /// </summary>
    private static byte[] ChainOfCalls(int values, int calls, byte[] call)
    {
        var body = new List<byte> { 0x00 };
        for (var i = 0; i < values; i++)
            body.AddRange([0x42, 0x00]);

        for (var i = 0; i < calls; i++)
            body.AddRange(call);

        for (var i = 0; i < values; i++)
            body.Add(0x1A);

        body.Add(0x0B);
        return body.ToArray();
    }

    /// <summary>A module of functions whose body is the same instructions a number of times over, after the locals given.</summary>
    private static byte[] Repeated(byte[] locals, byte[] instructions, int count, int functions)
    {
        var body = new List<byte>(locals);
        for (var i = 0; i < count; i++)
            body.AddRange(instructions);

        body.Add(0x0B);

        var bodies = new List<(int Type, byte[] Body)> { (0, NOTHING) };
        var heavy = body.ToArray();
        for (var i = 0; i < functions; i++)
            bodies.Add((0, heavy));

        return ShapeModules.Build([([], [])], bodies, 1);
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
