using System.Text;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// Hand-assembled WASI command modules for the raw-wasi runtime tests. No toolchain in the
/// loop: the binary format is small enough to write directly, and each program stays
/// readable here. Every module imports fd_write, fd_read and proc_exit from
/// wasi_snapshot_preview1 (indices 0, 1, 2), defines one function (index 3, or 4 behind an
/// extra import), exports its memory (one page) and the start function.
/// </summary>
internal static class WasiModules
{
    #region Constants

    private const byte I32 = 0x7F;

    private const byte OP_BLOCK = 0x02;
    private const byte OP_LOOP = 0x03;
    private const byte OP_END = 0x0B;
    private const byte OP_BR = 0x0C;
    private const byte OP_BR_IF = 0x0D;
    private const byte OP_CALL = 0x10;
    private const byte OP_DROP = 0x1A;
    private const byte OP_I32_LOAD = 0x28;
    private const byte OP_I32_STORE = 0x36;
    private const byte OP_MEMORY_SIZE = 0x3F;
    private const byte OP_MEMORY_GROW = 0x40;
    private const byte OP_I32_CONST = 0x41;
    private const byte OP_I32_EQ = 0x46;
    private const byte OP_I32_SHR_U = 0x76;
    private const byte OP_REF_NULL = 0xD0;
    private const byte OP_PREFIX_FC = 0xFC;
    private const byte FC_TABLE_GROW = 0x0F;
    private const byte FC_TABLE_SIZE = 0x10;
    private const byte FUNCREF = 0x70;
    private const byte BLOCK_VOID = 0x40;

    private const byte LIMITS_MIN = 0x00;
    private const byte LIMITS_MIN_MAX = 0x01;
    private const byte LIMITS_64_BIT = 0x04;

    private const int TYPE_START = 2;
    private const int TYPE_TWO_TO_ONE = 3;

    /// <summary>What a table bomb grows by at a step: 65,536 elements.</summary>
    public const int TABLE_STEP_SHIFT = 16;

    /// <summary>The most the table bomb's own declaration lets it grow to: 64 times the sandbox's cap.</summary>
    private const uint TABLE_BOMB_MAXIMUM = 64 * 1_048_576;

    private const int FD_WRITE = 0;
    private const int FD_READ = 1;
    private const int PROC_EXIT = 2;

    private const int IOV_PTR = 0;
    private const int COUNT_PTR = 8;
    private const int BUF_PTR = 32;
    private const int BUF_CAPACITY = 60_000;

    private const string WASI = "wasi_snapshot_preview1";
    private const string START = "_start";

    #endregion

    #region Programs

    /// <summary>Writes the text to stdout and returns (exit 0).</summary>
    public static byte[] Hello(string text)
    {
        return Build([.. WriteStdout()], TextData(text), START);
    }

    /// <summary>Reads one chunk of stdin and writes it back to stdout.</summary>
    public static byte[] Echo()
    {
        var body = new List<byte>();
        body.AddRange([.. I32Const(0), .. I32Const(IOV_PTR), .. I32Const(1), .. I32Const(COUNT_PTR), .. Call(FD_READ), OP_DROP]);
        // iov.len = nread
        body.AddRange([.. I32Const(IOV_PTR + 4), .. I32Const(COUNT_PTR), OP_I32_LOAD, 0x02, 0x00, OP_I32_STORE, 0x02, 0x00]);
        body.AddRange(WriteStdout());
        return Build(body, Iovec(BUF_PTR, BUF_CAPACITY), START);
    }

    /// <summary>Exits with the code through proc_exit.</summary>
    public static byte[] Exit(int code)
    {
        return Build([.. I32Const(code), .. Call(PROC_EXIT)], [], START);
    }

    /// <summary>Loops forever: burns fuel until the budget trips.</summary>
    public static byte[] Spin()
    {
        return Build([OP_LOOP, BLOCK_VOID, OP_BR, 0x00, OP_END], [], START);
    }

    /// <summary>Loads from an address far past the memory: a hard out-of-bounds trap.</summary>
    public static byte[] OutOfBounds()
    {
        return Build([.. I32Const(0x7FFFFFF0), OP_I32_LOAD, 0x02, 0x00, OP_DROP], [], START);
    }

    /// <summary>Grows memory 16 pages at a time until the store refuses, then exits with the page count.</summary>
    public static byte[] GrowUntilFull()
    {
        var body = new List<byte>
        {
            OP_BLOCK, BLOCK_VOID,
            OP_LOOP, BLOCK_VOID
        };
        body.AddRange([.. I32Const(16), OP_MEMORY_GROW, 0x00, .. I32Const(-1), OP_I32_EQ, OP_BR_IF, 0x01, OP_BR, 0x00, OP_END, OP_END]);
        body.AddRange([OP_MEMORY_SIZE, 0x00, .. Call(PROC_EXIT)]);
        return Build(body, [], START);
    }

    /// <summary>The hello program exported under another name: no _start.</summary>
    public static byte[] NoStart(string text)
    {
        return Build([.. WriteStdout()], TextData(text), "run");
    }

    /// <summary>The hello program with an import nothing provides: instantiation fails.</summary>
    public static byte[] UnknownImport(string text)
    {
        return Build([.. WriteStdout()], TextData(text), START, extraImport: ("env", "missing", TYPE_START));
    }

    /// <summary>
    /// A table bomb: grows a table of function references 65,536 elements at a time until
    /// the host refuses, then exits with the number of steps it was given. A table is host
    /// memory the linear memory cap does not count.
    /// </summary>
    public static byte[] TableBomb()
    {
        var body = new List<byte>
        {
            OP_BLOCK, BLOCK_VOID,
            OP_LOOP, BLOCK_VOID
        };
        body.AddRange([OP_REF_NULL, FUNCREF, .. I32Const(1 << TABLE_STEP_SHIFT), OP_PREFIX_FC, FC_TABLE_GROW, 0x00]);
        body.AddRange([.. I32Const(-1), OP_I32_EQ, OP_BR_IF, 0x01, OP_BR, 0x00, OP_END, OP_END]);
        body.AddRange([OP_PREFIX_FC, FC_TABLE_SIZE, 0x00, .. I32Const(TABLE_STEP_SHIFT), OP_I32_SHR_U, .. Call(PROC_EXIT)]);

        return Build(body, [], START, tables: [0x01, FUNCREF, LIMITS_MIN_MAX, 0x00, .. Uleb(TABLE_BOMB_MAXIMUM)]);
    }

    /// <summary>The exit program with two linear memories: twice the memory cap, if it were let in.</summary>
    public static byte[] TwoMemories()
    {
        return Build([.. I32Const(0), .. Call(PROC_EXIT)], [], START, memories: [0x02, LIMITS_MIN, 0x01, LIMITS_MIN, 0x01]);
    }

    /// <summary>The exit program with a linear memory of 64-bit addresses.</summary>
    public static byte[] Memory64()
    {
        return Build([.. I32Const(0), .. Call(PROC_EXIT)], [], START, memories: [0x01, LIMITS_64_BIT, 0x01]);
    }

    /// <summary>The exit program with a table of 64-bit indices (one element: only the kind matters).</summary>
    public static byte[] Table64()
    {
        return Build([.. I32Const(0), .. Call(PROC_EXIT)], [], START, tables: [0x01, FUNCREF, LIMITS_64_BIT, 0x01]);
    }

    /// <summary>Asks to renumber descriptor 0 as 7 (the C library's dup2) and exits with the answer's errno.</summary>
    public static byte[] RenumberDescriptor()
    {
        // The extra import is function 3; the start function follows it.
        return Build([.. I32Const(0), .. I32Const(7), .. Call(3), .. Call(PROC_EXIT)], [], START,
            extraImport: (WASI, "fd_renumber", TYPE_TWO_TO_ONE));
    }

    /// <summary>A header followed by a section id that does not exist.</summary>
    public static byte[] Invalid()
    {
        return [0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00, 0xFF, 0x01, 0x00];
    }

    #endregion

    #region Assembly

    private static byte[] Build(IReadOnlyList<byte> body, IReadOnlyList<byte> data, string startExport,
        (string Module, string Name, int Type)? extraImport = null, IReadOnlyList<byte>? tables = null, IReadOnlyList<byte>? memories = null)
    {
        var startIndex = extraImport == null ? 3 : 4;

        var module = new List<byte> { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 };

        // type 0: (i32 i32 i32 i32) -> i32 ; type 1: (i32) -> () ; type 2: () -> () ; type 3: (i32 i32) -> i32,
        // declared only for the module that imports a function of that shape (the hello bytes are pinned).
        var types = new List<byte>
        {
            0x60, 0x04, I32, I32, I32, I32, 0x01, I32,
            0x60, 0x01, I32, 0x00,
            0x60, 0x00, 0x00
        };
        var typeCount = 3;
        if (extraImport is { Type: TYPE_TWO_TO_ONE })
        {
            types.AddRange([0x60, 0x02, I32, I32, 0x01, I32]);
            typeCount = 4;
        }

        module.AddRange(Section(1, [(byte)typeCount, .. types]));

        var imports = new List<byte>();
        imports.AddRange([.. Name(WASI), .. Name("fd_write"), 0x00, 0x00]);
        imports.AddRange([.. Name(WASI), .. Name("fd_read"), 0x00, 0x00]);
        imports.AddRange([.. Name(WASI), .. Name("proc_exit"), 0x00, 0x01]);
        if (extraImport is { } extra)
            imports.AddRange([.. Name(extra.Module), .. Name(extra.Name), 0x00, (byte)extra.Type]);
        module.AddRange(Section(2, [.. Uleb((uint)(extraImport == null ? 3 : 4)), .. imports]));

        module.AddRange(Section(3, [0x01, 0x02]));
        if (tables != null)
            module.AddRange(Section(4, tables));
        module.AddRange(Section(5, memories ?? [0x01, 0x00, 0x01]));
        module.AddRange(Section(7, [0x02, .. Name("memory"), 0x02, 0x00, .. Name(startExport), 0x00, .. Uleb((uint)startIndex)]));

        var code = new List<byte> { 0x00 };
        code.AddRange(body);
        code.Add(OP_END);
        module.AddRange(Section(10, [0x01, .. Uleb((uint)code.Count), .. code]));

        if (data.Count > 0)
            module.AddRange(Section(11, [0x01, 0x00, OP_I32_CONST, 0x00, OP_END, .. Uleb((uint)data.Count), .. data]));

        return module.ToArray();
    }

    private static IEnumerable<byte> WriteStdout()
    {
        return [.. I32Const(1), .. I32Const(IOV_PTR), .. I32Const(1), .. I32Const(COUNT_PTR), .. Call(FD_WRITE), OP_DROP];
    }

    /// <summary>The data segment of a text writer: the iovec at 0, the text at BUF_PTR.</summary>
    private static byte[] TextData(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return [.. Iovec(BUF_PTR, bytes.Length), .. new byte[BUF_PTR - 8], .. bytes];
    }

    private static byte[] Iovec(int buffer, int length)
    {
        var bytes = new byte[8];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), buffer);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), length);
        return bytes;
    }

    private static byte[] Section(byte id, IReadOnlyList<byte> content)
    {
        return [id, .. Uleb((uint)content.Count), .. content];
    }

    private static byte[] Name(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return [.. Uleb((uint)bytes.Length), .. bytes];
    }

    private static byte[] I32Const(int value)
    {
        return [OP_I32_CONST, .. Sleb(value)];
    }

    private static byte[] Call(int index)
    {
        return [OP_CALL, .. Uleb((uint)index)];
    }

    private static List<byte> Uleb(uint value)
    {
        var bytes = new List<byte>();
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
                b |= 0x80;
            bytes.Add(b);
        } while (value != 0);

        return bytes;
    }

    private static List<byte> Sleb(int value)
    {
        var bytes = new List<byte>();
        var more = true;
        while (more)
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            var signBitSet = (b & 0x40) != 0;
            if ((value == 0 && !signBitSet) || (value == -1 && signBitSet))
                more = false;
            else
                b |= 0x80;
            bytes.Add(b);
        }

        return bytes;
    }

    #endregion
}
