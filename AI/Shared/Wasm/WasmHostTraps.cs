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
    /// a trap with its code when it stops the guest's own code, and that code is the answer. It
    /// can also stop the guest inside a call that passes through the host: the host notes that
    /// where it sees the trap (<see cref="WasmHostMeter.Interrupted"/>); what is left for this
    /// test is an error of the engine whose text ends with the cause
    /// (<see cref="INTERRUPT_CAUSE"/>). A module can put any words into an error's text (the name
    /// of an import, the name of a function in a backtrace), so the text counts only as the
    /// engine's own last line, and only once the run's deadline has come by the host's clock:
    /// before that no deadline can have fired, whatever an error says.
    /// </summary>
    /// <param name="exception">What the engine threw.</param>
    /// <param name="deadlineReached">Whether the run's wall time is over by the host's own clock.</param>
    /// <returns>True when the exception is the deadline's trap, or names it as its cause after the deadline.</returns>
    public static bool IsInterrupt(Exception exception, bool deadlineReached)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is TrapException { Type: TrapCode.Interrupt })
                return true;

            if (deadlineReached && string.Equals(LastLine(current.Message), INTERRUPT_CAUSE, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>The last line of a text that holds anything, without the blanks around it.</summary>
    private static string LastLine(string text)
    {
        var lines = text.Split((char)10);
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index].Trim();
            if (line.Length > 0)
                return line;
        }

        return "";
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
