using OutWit.Controller.AI.Verify.Tests.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// The guests of the conversation tests, written in the WebAssembly text format: programs that
/// read their stdin and answer on their stdout, each the smallest that shows one thing. A guest
/// has one page of memory; a buffer it reads into starts at <see cref="BUFFER"/>.
/// </summary>
internal static class WasmHostConversationModules
{
    #region Constants

    /// <summary>What the echo guest puts before every piece it read.</summary>
    public const string PREFIX = "> ";

    /// <summary>The echo guest exits with this plus the errno when a read fails.</summary>
    public const int READ_FAILED = 100;

    /// <summary>Where a guest's read buffer starts.</summary>
    public const int BUFFER = 64;

    /// <summary>The bytes of a guest's memory: one page.</summary>
    public const int MEMORY_BYTES = 65536;

    private const string WASI = "wasi_snapshot_preview1";

    private const string IMPORTS = $"""
          (import "{WASI}" "fd_read" (func $read (param i32 i32 i32 i32) (result i32)))
          (import "{WASI}" "fd_write" (func $write (param i32 i32 i32 i32) (result i32)))
          (import "{WASI}" "fd_close" (func $close (param i32) (result i32)))
          (import "{WASI}" "proc_exit" (func $exit (param i32)))
          (memory (export "memory") 1)
        """;

    #endregion

    #region Programs

    /// <summary>
    /// Reads its stdin piece by piece and writes each piece back behind <see cref="PREFIX"/>,
    /// until a read gives nothing; exits with the number of pieces it read
    /// (<see cref="READ_FAILED"/> plus the errno when a read fails).
    /// </summary>
    /// <param name="capacity">The bytes of its read buffer: the most one piece holds.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Echo(int capacity)
    {
        // The read's buffer is at 64; the write starts two bytes before it, at the prefix.
        return HostileModules.Bytes($$"""
            (module
            {{IMPORTS}}
              (data (i32.const 62) "{{PREFIX}}")
              (func (export "_start") (local $pieces i32) (local $errno i32)
                (i32.store (i32.const 0) (i32.const {{BUFFER}}))
                (i32.store (i32.const 4) (i32.const {{capacity}}))
                (i32.store (i32.const 16) (i32.const 62))
                (block $end
                  (loop $next
                    (local.set $errno (call $read (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 8)))
                    (if (local.get $errno)
                      (then (call $exit (i32.add (i32.const {{READ_FAILED}}) (local.get $errno)))))
                    (br_if $end (i32.eqz (i32.load (i32.const 8))))
                    (local.set $pieces (i32.add (local.get $pieces) (i32.const 1)))
                    (i32.store (i32.const 20) (i32.add (i32.load (i32.const 8)) (i32.const 2)))
                    (drop (call $write (i32.const 1) (i32.const 16) (i32.const 1) (i32.const 24)))
                    (br $next)))
                (call $exit (local.get $pieces))))
            """);
    }

    /// <summary>
    /// Reads a descriptor once, with one buffer record at address 0, and exits with the errno
    /// the read answered.
    /// </summary>
    /// <param name="descriptor">The descriptor read.</param>
    /// <param name="pointer">Where the record says the buffer starts.</param>
    /// <param name="length">How long the record says the buffer is.</param>
    /// <param name="buffers">Where the read is told the list of records starts.</param>
    /// <param name="count">How many records the read is told the list has.</param>
    /// <param name="read">Where the read is told to put the number of bytes.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] ReadOnce(int descriptor, int pointer, int length, int buffers, int count, int read)
    {
        return HostileModules.Bytes($$"""
            (module
            {{IMPORTS}}
              (func (export "_start")
                (i32.store (i32.const 0) (i32.const {{pointer}}))
                (i32.store (i32.const 4) (i32.const {{length}}))
                (call $exit (call $read (i32.const {{descriptor}}) (i32.const {{buffers}}) (i32.const {{count}}) (i32.const {{read}})))))
            """);
    }

    /// <summary>
    /// Reads its stdin twice with a list of three buffers (an empty one, four bytes at 64, eight
    /// bytes at 68 that start out as dots), writes the twelve bytes after each read, and exits
    /// with ten times what the first read gave plus what the second gave.
    /// </summary>
    /// <returns>The module's bytes.</returns>
    public static byte[] Vector()
    {
        return HostileModules.Bytes($$"""
            (module
            {{IMPORTS}}
              (data (i32.const 68) "........")
              (func (export "_start") (local $first i32)
                (i32.store (i32.const 0) (i32.const 64)) (i32.store (i32.const 4) (i32.const 0))
                (i32.store (i32.const 8) (i32.const 64)) (i32.store (i32.const 12) (i32.const 4))
                (i32.store (i32.const 16) (i32.const 68)) (i32.store (i32.const 20) (i32.const 8))
                (i32.store (i32.const 32) (i32.const 64)) (i32.store (i32.const 36) (i32.const 12))
                (drop (call $read (i32.const 0) (i32.const 0) (i32.const 3) (i32.const 24)))
                (local.set $first (i32.load (i32.const 24)))
                (drop (call $write (i32.const 1) (i32.const 32) (i32.const 1) (i32.const 40)))
                (drop (call $read (i32.const 0) (i32.const 0) (i32.const 3) (i32.const 24)))
                (drop (call $write (i32.const 1) (i32.const 32) (i32.const 1) (i32.const 40)))
                (call $exit (i32.add (i32.mul (local.get $first) (i32.const 10)) (i32.load (i32.const 24))))))
            """);
    }

    /// <summary>
    /// Reads its stdin a fixed number of times, whatever the reads give, and exits with the
    /// bytes they gave together.
    /// </summary>
    /// <param name="reads">How many times it reads.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] ReadTimes(int reads)
    {
        return HostileModules.Bytes($$"""
            (module
            {{IMPORTS}}
              (func (export "_start") (local $i i32) (local $total i32)
                (i32.store (i32.const 0) (i32.const {{BUFFER}}))
                (i32.store (i32.const 4) (i32.const 16))
                (loop $next
                  (i32.store (i32.const 8) (i32.const 0))
                  (drop (call $read (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 8)))
                  (local.set $total (i32.add (local.get $total) (i32.load (i32.const 8))))
                  (local.set $i (i32.add (local.get $i) (i32.const 1)))
                  (br_if $next (i32.lt_u (local.get $i) (i32.const {{reads}}))))
                (call $exit (local.get $total))))
            """);
    }

    /// <summary>
    /// Closes its stdin, reads it, and exits with a hundred times what the close answered plus
    /// what the read answered.
    /// </summary>
    /// <returns>The module's bytes.</returns>
    public static byte[] CloseThenRead()
    {
        return HostileModules.Bytes($$"""
            (module
            {{IMPORTS}}
              (func (export "_start") (local $closed i32)
                (i32.store (i32.const 0) (i32.const {{BUFFER}}))
                (i32.store (i32.const 4) (i32.const 16))
                (local.set $closed (call $close (i32.const 0)))
                (call $exit (i32.add (i32.mul (local.get $closed) (i32.const 100))
                                     (call $read (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 8))))))
            """);
    }

    #endregion
}
