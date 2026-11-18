using Wasmtime;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The fixed words a crash is named in. A guest that trapped said nothing about it, so the host
/// says it, the same way on every node and every platform (the engine's own messages carry
/// addresses and backtraces). A budget stop is not named: one node may stop on fuel where another
/// stops on the clock, and the outputs must not tell them apart.
/// </summary>
internal static class WasmHostTraps
{
    #region Constants

    /// <summary>What a crash's fault text starts with.</summary>
    public const string FAULT_PREFIX = "wasm trap: ";

    /// <summary>
    /// The line wasmtime closes an error with when its cause is the epoch deadline: the words of
    /// <see cref="TrapCode.Interrupt"/> in the engine's own text ("Caused by: wasm trap: interrupt").
    /// </summary>
    public const string INTERRUPT_CAUSE = "wasm trap: interrupt";

    #endregion

    #region Functions

    /// <summary>
    /// The fault text of a trap: the fixed words of a crash, or null for a budget stop.
    /// </summary>
    /// <param name="code">The trap that ended a run.</param>
    /// <returns>"wasm trap: ..." for a crash; null for out of fuel and an interrupt.</returns>
    public static string? Fault(TrapCode code)
    {
        return IsBudgetStop(code) ? null : $"{FAULT_PREFIX}{Text(code)}";
    }

    /// <summary>Whether a trap is a budget stop (out of fuel, or the epoch deadline) rather than a crash.</summary>
    /// <param name="code">The trap.</param>
    /// <returns>True for <see cref="TrapCode.OutOfFuel"/> and <see cref="TrapCode.Interrupt"/>.</returns>
    public static bool IsBudgetStop(TrapCode code)
    {
        return code is TrapCode.OutOfFuel or TrapCode.Interrupt;
    }

    /// <summary>
    /// Whether an error of the engine is the epoch deadline. The clock's trap reaches the host as
    /// a trap when it stops the guest's own code, and as an error of another kind when it stops
    /// the guest inside a call that passes through the host (its text then ends with the cause,
    /// <see cref="INTERRUPT_CAUSE"/>). Where the clock fires is a matter of the machine, so both
    /// are the same stop: the clock, never a verdict about the program.
    /// </summary>
    /// <param name="exception">What the engine threw.</param>
    /// <returns>True when the exception, or one it wraps, is the deadline's trap or names it as its cause.</returns>
    public static bool IsInterrupt(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is TrapException { Type: TrapCode.Interrupt })
                return true;

            foreach (var line in current.Message.Split((char)10))
            {
                if (string.Equals(line.Trim(), INTERRUPT_CAUSE, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    /// <summary>The words for one kind of trap.</summary>
    /// <param name="code">The trap.</param>
    /// <returns>A short description; the code's name for a kind without words of its own.</returns>
    public static string Text(TrapCode code)
    {
        return code switch
        {
            TrapCode.MemoryOutOfBounds => "out of bounds memory access (a pointer outside the memory, or a stack that overflowed)",
            TrapCode.StackOverflow => "call stack exhausted",
            TrapCode.Unreachable => "unreachable code reached (abort, a failed assertion, a trap instruction)",
            TrapCode.IntegerDivisionByZero => "integer division by zero",
            TrapCode.IntegerOverflow => "integer overflow",
            TrapCode.BadConversionToInteger => "invalid conversion to integer",
            TrapCode.IndirectCallToNull => "call through a null function pointer",
            TrapCode.BadSignature => "call through a function pointer of the wrong type",
            TrapCode.TableOutOfBounds => "out of bounds table access",
            TrapCode.HeapMisaligned => "misaligned memory access",
            _ => code.ToString()
        };
    }

    #endregion
}
