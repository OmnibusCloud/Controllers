using System.Text;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The modules of the hostile tests, written in the WebAssembly text format and turned into
/// bytes by the engine's own converter: each is the input an audit used, or the smallest one
/// that does the same thing.
/// </summary>
internal static class HostileModules
{
    #region Constants

    private const string WASI = "wasi_snapshot_preview1";

    #endregion

    #region Programs

    /// <summary>
    /// Writes a 64 MiB buffer to a descriptor a number of times: a few hundred units of fuel for
    /// gigabytes of output.
    /// </summary>
    /// <param name="descriptor">1 for stdout, 2 for stderr.</param>
    /// <param name="calls">How many times the buffer is written.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Flood(int descriptor, int calls)
    {
        return Bytes($$"""
            (module
              (import "{{WASI}}" "fd_write" (func $w (param i32 i32 i32 i32) (result i32)))
              (memory (export "memory") 1024)
              (func (export "_start") (local $i i32)
                (i32.store (i32.const 0) (i32.const 65536))
                (i32.store (i32.const 4) (i32.const 67043328))
                (loop $n
                  (drop (call $w (i32.const {{descriptor}}) (i32.const 0) (i32.const 1) (i32.const 8)))
                  (local.set $i (i32.add (local.get $i) (i32.const 1)))
                  (br_if $n (i32.lt_u (local.get $i) (i32.const {{calls}}))))))
            """);
    }

    /// <summary>Prints a line of a given length to stdout and returns.</summary>
    /// <param name="bytes">The bytes written, in one call.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Print(int bytes)
    {
        return Bytes($$"""
            (module
              (import "{{WASI}}" "fd_write" (func $w (param i32 i32 i32 i32) (result i32)))
              (memory (export "memory") 256)
              (func (export "_start")
                (memory.fill (i32.const 65536) (i32.const 120) (i32.const {{bytes}}))
                (i32.store (i32.const 0) (i32.const 65536))
                (i32.store (i32.const 4) (i32.const {{bytes}}))
                (drop (call $w (i32.const 1) (i32.const 0) (i32.const 1) (i32.const 8)))))
            """);
    }

    /// <summary>Calls a WASI function of two i32 arguments and exits with what it answered.</summary>
    /// <param name="function">The function: random_get(buffer, length).</param>
    /// <param name="first">The first argument.</param>
    /// <param name="second">The second argument.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] ExitWith(string function, int first, int second)
    {
        return Bytes($$"""
            (module
              (import "{{WASI}}" "{{function}}" (func $f (param i32 i32) (result i32)))
              (import "{{WASI}}" "proc_exit" (func $exit (param i32)))
              (memory (export "memory") 1)
              (func (export "_start")
                (call $exit (call $f (i32.const {{first}}) (i32.const {{second}})))))
            """);
    }

    /// <summary>Calls clock_time_get with a pointer and exits with what it answered.</summary>
    /// <param name="pointer">Where the time is to be written.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] ClockTo(int pointer)
    {
        return Bytes($$"""
            (module
              (import "{{WASI}}" "clock_time_get" (func $clock (param i32 i64 i32) (result i32)))
              (import "{{WASI}}" "proc_exit" (func $exit (param i32)))
              (memory (export "memory") 1)
              (func (export "_start")
                (call $exit (call $clock (i32.const 0) (i64.const 1) (i32.const {{pointer}})))))
            """);
    }

    /// <summary>Fills 250 MiB with random bytes a number of times: seconds of the host's time for a handful of instructions.</summary>
    /// <param name="calls">How many times.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] RandomFlood(int calls)
    {
        return Bytes($$"""
            (module
              (import "{{WASI}}" "random_get" (func $random (param i32 i32) (result i32)))
              (memory (export "memory") 4096)
              (func (export "_start") (local $i i32)
                (loop $n
                  (drop (call $random (i32.const 65536) (i32.const 262144000)))
                  (local.set $i (i32.add (local.get $i) (i32.const 1)))
                  (br_if $n (i32.lt_u (local.get $i) (i32.const {{calls}}))))))
            """);
    }

    /// <summary>Loops without end.</summary>
    /// <returns>The module's bytes.</returns>
    public static byte[] Spin()
    {
        return Bytes("""
            (module
              (memory (export "memory") 1)
              (func (export "_start") (local $i i64)
                (loop $n
                  (local.set $i (i64.add (local.get $i) (i64.const 1)))
                  (br $n))))
            """);
    }

    /// <summary>
    /// The shape the engine's compiler chokes on: functions with a number of locals, nested
    /// loops, and a read of every local inside them.
    /// </summary>
    /// <param name="locals">The locals of one function, each read once in the innermost loop.</param>
    /// <param name="loops">The loops of one function, nested.</param>
    /// <param name="functions">How many such functions.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] NestedLoops(int locals, int loops, int functions = 1)
    {
        var body = new List<byte>();
        body.AddRange(Leb(1));
        body.AddRange(Leb(locals));
        body.Add(0x7E);
        for (var i = 0; i < loops; i++)
        {
            body.Add(0x03);
            body.Add(0x40);
        }

        for (var i = 0; i < locals; i++)
        {
            body.Add(0x20);
            body.AddRange(Leb(i));
            body.Add(0x1A);
        }

        for (var i = 0; i <= loops; i++)
            body.Add(0x0B);

        return Module(body, functions);
    }

    /// <summary>Blocks nested a number of levels deep, and nothing in them.</summary>
    /// <param name="depth">The levels.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] NestedBlocks(int depth)
    {
        var body = new List<byte>(Leb(0));
        for (var i = 0; i < depth; i++)
        {
            body.Add(0x02);
            body.Add(0x40);
        }

        for (var i = 0; i <= depth; i++)
            body.Add(0x0B);

        return Module(body, 1);
    }

    /// <summary>Many small functions without loops: honest in shape, and work for a compiler.</summary>
    /// <param name="functions">How many functions.</param>
    /// <param name="operations">The additions in each.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] ManyFunctions(int functions, int operations)
    {
        var body = new List<byte>();
        body.AddRange(Leb(1));
        body.AddRange(Leb(4));
        body.Add(0x7E);
        for (var i = 0; i < operations; i++)
        {
            body.Add(0x20);
            body.Add((byte)(i % 4));
            body.Add(0x42);
            body.Add((byte)(i % 60 + 1));
            body.Add(0x7C);
            body.Add(0x21);
            body.Add((byte)((i + 1) % 4));
        }

        body.Add(0x0B);
        return Module(body, functions);
    }

    #endregion

    #region Assembly

    /// <summary>A module in the text format as bytes.</summary>
    /// <param name="text">The module's text.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Bytes(string text)
    {
        return Wasmtime.Module.ConvertText(text);
    }

    /// <summary>
    /// A command module of one function type, () to (): the same body a number of times, the
    /// first exported as the entry, with a memory of one page.
    /// </summary>
    private static byte[] Module(IReadOnlyList<byte> body, int functions)
    {
        var module = new List<byte> { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 };
        Section(module, 1, [.. Leb(1), 0x60, 0x00, 0x00]);

        var declared = new List<byte>(Leb(functions));
        for (var i = 0; i < functions; i++)
            declared.Add(0x00);
        Section(module, 3, declared);

        Section(module, 5, [0x01, 0x00, 0x01]);

        var exports = new List<byte>(Leb(2));
        exports.AddRange(Name("memory"));
        exports.Add(0x02);
        exports.Add(0x00);
        exports.AddRange(Name("_start"));
        exports.Add(0x00);
        exports.Add(0x00);
        Section(module, 7, exports);

        var code = new List<byte>(Leb(functions));
        for (var i = 0; i < functions; i++)
        {
            code.AddRange(Leb(body.Count));
            code.AddRange(body);
        }

        Section(module, 10, code);
        return module.ToArray();
    }

    private static void Section(List<byte> module, byte id, IReadOnlyList<byte> content)
    {
        module.Add(id);
        module.AddRange(Leb(content.Count));
        module.AddRange(content);
    }

    private static byte[] Name(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        return [.. Leb(bytes.Length), .. bytes];
    }

    private static byte[] Leb(int value)
    {
        var result = new List<byte>();
        var left = (uint)value;
        do
        {
            var part = (byte)(left & 0x7F);
            left >>= 7;
            if (left != 0)
                part |= 0x80;
            result.Add(part);
        } while (left != 0);

        return result.ToArray();
    }

    #endregion
}
