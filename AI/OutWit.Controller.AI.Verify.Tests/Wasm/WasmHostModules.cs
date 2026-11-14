using System.Text;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// A hand-assembled WASI command module in the shape of a host's in/out contract: it opens
/// <c>doc.txt</c> in the folder preopened first (descriptor 3, the guest's <c>/in</c>), reads it,
/// and writes the bytes to <c>copy.txt</c> in the folder preopened second (descriptor 4, the
/// guest's <c>/out</c>). Exit 0 when both went through, <see cref="INPUT_FAILED"/> when the input
/// could not be opened, <see cref="OUTPUT_FAILED"/> when the output could not.
/// </summary>
internal static class WasmHostModules
{
    #region Constants

    /// <summary>The file the module reads from the first folder.</summary>
    public const string INPUT_NAME = "doc.txt";

    /// <summary>The file the module writes into the second folder.</summary>
    public const string OUTPUT_NAME = "copy.txt";

    /// <summary>The exit code when the input could not be opened.</summary>
    public const int INPUT_FAILED = 3;

    /// <summary>The exit code when the output could not be opened.</summary>
    public const int OUTPUT_FAILED = 4;

    /// <summary>The most bytes the module copies.</summary>
    public const int BUFFER_CAPACITY = 4096;

    private const byte I32 = 0x7F;
    private const byte I64 = 0x7E;

    private const byte OP_IF = 0x04;
    private const byte OP_END = 0x0B;
    private const byte OP_CALL = 0x10;
    private const byte OP_DROP = 0x1A;
    private const byte OP_I32_LOAD = 0x28;
    private const byte OP_I32_STORE = 0x36;
    private const byte OP_I32_CONST = 0x41;
    private const byte OP_I64_CONST = 0x42;
    private const byte BLOCK_VOID = 0x40;

    private const int FD_WRITE = 0;
    private const int FD_READ = 1;
    private const int PROC_EXIT = 2;
    private const int PATH_OPEN = 3;
    private const int START = 4;

    private const int IN_FOLDER = 3;
    private const int OUT_FOLDER = 4;

    private const int IOV_PTR = 0;
    private const int COUNT_PTR = 8;
    private const int FD_IN_PTR = 12;
    private const int FD_OUT_PTR = 16;
    private const int IN_NAME_PTR = 32;
    private const int OUT_NAME_PTR = 48;
    private const int BUF_PTR = 64;

    // WASI preview 1: oflags creat | trunc, and the rights to read and to write a descriptor.
    private const int OFLAGS_CREATE_TRUNCATE = 0x1 | 0x8;
    private const long RIGHT_FD_READ = 1L << 1;
    private const long RIGHT_FD_WRITE = 1L << 6;

    private const string WASI = "wasi_snapshot_preview1";

    #endregion

    #region Programs

    /// <summary>The copy program.</summary>
    /// <returns>The module's bytes.</returns>
    public static byte[] Copy()
    {
        var body = new List<byte>();

        body.AddRange(Open(IN_FOLDER, IN_NAME_PTR, INPUT_NAME.Length, 0, RIGHT_FD_READ, FD_IN_PTR, INPUT_FAILED));

        // fd_read(fd_in, iov, 1, &count); iov.len = count
        body.AddRange([.. I32Const(FD_IN_PTR), OP_I32_LOAD, 0x02, 0x00, .. I32Const(IOV_PTR), .. I32Const(1), .. I32Const(COUNT_PTR), OP_CALL, FD_READ, OP_DROP]);
        body.AddRange([.. I32Const(IOV_PTR + 4), .. I32Const(COUNT_PTR), OP_I32_LOAD, 0x02, 0x00, OP_I32_STORE, 0x02, 0x00]);

        body.AddRange(Open(OUT_FOLDER, OUT_NAME_PTR, OUTPUT_NAME.Length, OFLAGS_CREATE_TRUNCATE, RIGHT_FD_WRITE, FD_OUT_PTR, OUTPUT_FAILED));

        // fd_write(fd_out, iov, 1, &count); proc_exit(0)
        body.AddRange([.. I32Const(FD_OUT_PTR), OP_I32_LOAD, 0x02, 0x00, .. I32Const(IOV_PTR), .. I32Const(1), .. I32Const(COUNT_PTR), OP_CALL, FD_WRITE, OP_DROP]);
        body.AddRange([.. I32Const(0), OP_CALL, PROC_EXIT]);

        return Build(body);
    }

    #endregion

    #region Assembly

    /// <summary>path_open in a folder; a nonzero errno exits with the failure's code.</summary>
    private static IEnumerable<byte> Open(int folder, int namePtr, int nameLength, int oflags, long rights, int fdPtr, int failureCode)
    {
        return
        [
            .. I32Const(folder), .. I32Const(0), .. I32Const(namePtr), .. I32Const(nameLength), .. I32Const(oflags),
            .. I64Const(rights), .. I64Const(0), .. I32Const(0), .. I32Const(fdPtr), OP_CALL, PATH_OPEN,
            OP_IF, BLOCK_VOID, .. I32Const(failureCode), OP_CALL, PROC_EXIT, OP_END
        ];
    }

    private static byte[] Build(IReadOnlyList<byte> body)
    {
        var module = new List<byte> { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 };

        // type 0: (i32 i32 i32 i32) -> i32 ; type 1: (i32) -> () ; type 2: () -> () ;
        // type 3: path_open (i32 i32 i32 i32 i32 i64 i64 i32 i32) -> i32
        module.AddRange(Section(1,
        [
            0x04,
            0x60, 0x04, I32, I32, I32, I32, 0x01, I32,
            0x60, 0x01, I32, 0x00,
            0x60, 0x00, 0x00,
            0x60, 0x09, I32, I32, I32, I32, I32, I64, I64, I32, I32, 0x01, I32
        ]));

        module.AddRange(Section(2,
        [
            0x04,
            .. Name(WASI), .. Name("fd_write"), 0x00, 0x00,
            .. Name(WASI), .. Name("fd_read"), 0x00, 0x00,
            .. Name(WASI), .. Name("proc_exit"), 0x00, 0x01,
            .. Name(WASI), .. Name("path_open"), 0x00, 0x03
        ]));

        module.AddRange(Section(3, [0x01, 0x02]));
        module.AddRange(Section(5, [0x01, 0x00, 0x01]));
        module.AddRange(Section(7, [0x02, .. Name("memory"), 0x02, 0x00, .. Name("_start"), 0x00, START]));

        // No locals, then the body.
        var code = new List<byte> { 0x00 };
        code.AddRange(body);
        code.Add(OP_END);
        module.AddRange(Section(10, [0x01, .. Uleb((uint)code.Count), .. code]));

        var data = new byte[BUF_PTR];
        BitConverter.TryWriteBytes(data.AsSpan(IOV_PTR, 4), BUF_PTR);
        BitConverter.TryWriteBytes(data.AsSpan(IOV_PTR + 4, 4), BUFFER_CAPACITY);
        Encoding.UTF8.GetBytes(INPUT_NAME).CopyTo(data, IN_NAME_PTR);
        Encoding.UTF8.GetBytes(OUTPUT_NAME).CopyTo(data, OUT_NAME_PTR);
        module.AddRange(Section(11, [0x01, 0x00, OP_I32_CONST, 0x00, OP_END, .. Uleb((uint)data.Length), .. data]));

        return module.ToArray();
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

    private static byte[] I64Const(long value)
    {
        return [OP_I64_CONST, .. Sleb(value)];
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

    private static List<byte> Sleb(long value)
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
