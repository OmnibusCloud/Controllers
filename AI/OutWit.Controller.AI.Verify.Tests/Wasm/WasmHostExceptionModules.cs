using OutWit.Controller.AI.Verify.Tests.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// The guests of the exception tests, written in the WebAssembly text format with the standard
/// exceptions proposal (a tag, <c>throw</c>, <c>try_table</c>): what a C++ program compiled for
/// WebAssembly exceptions is made of. Each exits through <c>proc_exit</c> with a code it computes
/// after its last catch.
/// </summary>
internal static class WasmHostExceptionModules
{
    #region Constants

    /// <summary>What <see cref="ThrowAndCatch"/> exits with: the value it threw and two it adds after the catch.</summary>
    public const int CAUGHT_EXIT = 42;

    /// <summary>What a burner exits with when it runs to its end.</summary>
    public const int FINISHED_EXIT = 7;

    private const string WASI = "wasi_snapshot_preview1";

    private const string HEAD = $"""
          (import "{WASI}" "proc_exit" (func $exit (param i32)))
          (memory (export "memory") 1)
        """;

    #endregion

    #region Programs

    /// <summary>
    /// A callee throws 40, its caller catches the value, adds 2 and exits with the sum.
    /// </summary>
    /// <returns>The module's bytes.</returns>
    public static byte[] ThrowAndCatch()
    {
        return HostileModules.Bytes($$"""
            (module
            {{HEAD}}
              (tag $e (param i32))
              (func $thrower (param $value i32)
                (throw $e (local.get $value)))
              (func (export "_start")
                (call $exit
                  (i32.add
                    (block $caught (result i32)
                      (try_table (catch $e $caught)
                        (call $thrower (i32.const 40)))
                      (i32.const 0))
                    (i32.const 2)))))
            """);
    }

    /// <summary>
    /// Catches one exception and then, in the handler, turns a loop a number of times; exits with
    /// <see cref="FINISHED_EXIT"/> when the loop ends.
    /// </summary>
    /// <param name="thrownByCallee">Whether the exception comes from a function called in the guarded block, or from the block itself.</param>
    /// <param name="turns">The turns of the handler's loop.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] BurnInHandler(bool thrownByCallee, long turns)
    {
        var thrown = thrownByCallee ? "(call $thrower)" : "(throw $e)";
        return HostileModules.Bytes($$"""
            (module
            {{HEAD}}
              (tag $e)
              (func $thrower (throw $e))
              (func (export "_start") (local $i i64)
                (block $handler
                  (try_table (catch $e $handler)
                    {{thrown}})
                  (call $exit (i32.const 1)))
                (loop $burn
                  (local.set $i (i64.add (local.get $i) (i64.const 1)))
                  (br_if $burn (i64.lt_u (local.get $i) (i64.const {{turns}}))))
                (call $exit (i32.const {{FINISHED_EXIT}}))))
            """);
    }

    /// <summary>
    /// A callee turns a loop a number of times and then throws; its caller catches, and calls it
    /// again, a number of rounds; exits with <see cref="FINISHED_EXIT"/> after the last. A turn of
    /// the callee's loop is eight instructions.
    /// </summary>
    /// <param name="turns">The turns of the callee's loop before it throws.</param>
    /// <param name="rounds">How many times the callee is called.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] BurnThenThrow(long turns, long rounds)
    {
        return HostileModules.Bytes($$"""
            (module
            {{HEAD}}
              (tag $e)
              (func $thrower (local $i i64)
                (loop $burn
                  (local.set $i (i64.add (local.get $i) (i64.const 1)))
                  (br_if $burn (i64.lt_u (local.get $i) (i64.const {{turns}}))))
                (throw $e))
              (func (export "_start") (local $round i64)
                (loop $again
                  (block $handler
                    (try_table (catch $e $handler)
                      (call $thrower)))
                  (local.set $round (i64.add (local.get $round) (i64.const 1)))
                  (br_if $again (i64.lt_u (local.get $round) (i64.const {{rounds}}))))
                (call $exit (i32.const {{FINISHED_EXIT}}))))
            """);
    }

    /// <summary>
    /// Calls a function a number of times with the turn's number and adds up what comes back;
    /// exits with the low seven bits of the sum (<see cref="SumExit"/>). The function throws its
    /// argument, and the caller catches it; or, for comparison, it returns its argument.
    /// </summary>
    /// <param name="times">How many times.</param>
    /// <param name="throws">Whether the value comes back as an exception or as a result.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Sum(int times, bool throws)
    {
        var callee = throws
            ? "(tag $e (param i32)) (func $give (param $value i32) (throw $e (local.get $value)))"
            : "(func $give (param $value i32) (result i32) (local.get $value))";
        var given = throws
            ? "(block $caught (result i32) (try_table (catch $e $caught) (call $give (local.get $i))) (i32.const 0))"
            : "(call $give (local.get $i))";

        return HostileModules.Bytes($$"""
            (module
            {{HEAD}}
              {{callee}}
              (func (export "_start") (local $i i32) (local $sum i32)
                (loop $again
                  (local.set $sum (i32.add (local.get $sum) {{given}}))
                  (local.set $i (i32.add (local.get $i) (i32.const 1)))
                  (br_if $again (i32.lt_u (local.get $i) (i32.const {{times}}))))
                (call $exit (i32.and (local.get $sum) (i32.const 127)))))
            """);
    }

    /// <summary>What <see cref="Sum"/> exits with.</summary>
    /// <param name="times">How many values it adds up: 0, 1, 2 and so on.</param>
    /// <returns>The exit code.</returns>
    public static int SumExit(int times)
    {
        return (int)((long)times * (times - 1) / 2 & 127);
    }

    /// <summary>
    /// Throws a number of exceptions of four 64-bit values each and keeps every one it catches,
    /// in a table that grows by one an exception; exits with <see cref="FINISHED_EXIT"/> when it
    /// has kept them all, and with 1 when the table is refused growth.
    /// </summary>
    /// <param name="times">How many exceptions it keeps.</param>
    /// <returns>The module's bytes.</returns>
    public static byte[] Keep(int times)
    {
        return HostileModules.Bytes($$"""
            (module
            {{HEAD}}
              (tag $e (param i64 i64 i64 i64))
              (table $kept 0 exnref)
              (func (export "_start") (local $i i32) (local $caught exnref)
                (loop $again
                  (local.set $caught
                    (block $handler (result exnref)
                      (try_table (catch_all_ref $handler)
                        (throw $e (i64.const 7) (i64.const 7) (i64.const 7) (i64.const 7)))
                      (ref.null exn)))
                  (if (i32.eq (table.grow $kept (local.get $caught) (i32.const 1)) (i32.const -1))
                    (then (call $exit (i32.const 1))))
                  (local.set $i (i32.add (local.get $i) (i32.const 1)))
                  (br_if $again (i32.lt_u (local.get $i) (i32.const {{times}}))))
                (call $exit (i32.const {{FINISHED_EXIT}}))))
            """);
    }

    /// <summary>A module that declares a struct type: the GC proposal.</summary>
    /// <returns>The module's bytes.</returns>
    public static byte[] GcStruct()
    {
        return HostileModules.Bytes($$"""
            (module
            {{HEAD}}
              (type $s (struct (field i32)))
              (func (export "_start")))
            """);
    }

    /// <summary>A module that calls through a typed function reference: the function references proposal.</summary>
    /// <returns>The module's bytes.</returns>
    public static byte[] TypedReference()
    {
        return HostileModules.Bytes($$"""
            (module
            {{HEAD}}
              (type $t (func (result i32)))
              (func $f (type $t) (i32.const 7))
              (elem declare func $f)
              (func (export "_start") (drop (call_ref $t (ref.func $f)))))
            """);
    }

    #endregion
}
