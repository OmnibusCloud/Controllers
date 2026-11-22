namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Reads the code of a module the engine has validated: the numbers of its binary format and the
/// immediates of its instructions, so that a scan can step from one instruction to the next
/// (<see cref="VerifyModuleShape"/>). An instruction it does not know is reported, never guessed at.
/// </summary>
internal static class VerifyModuleCode
{
    #region Constants

    private const byte IMPORT_FUNCTION = 0x00;
    private const byte IMPORT_TABLE = 0x01;
    private const byte IMPORT_MEMORY = 0x02;
    private const byte IMPORT_GLOBAL = 0x03;

    #endregion

    #region Functions

    /// <summary>Steps over the immediates of an instruction that neither opens nor closes a block.</summary>
    /// <param name="op">The instruction's first byte.</param>
    /// <param name="body">The function's body.</param>
    /// <param name="position">Where the immediates start; moved past them.</param>
    /// <returns>False for an instruction the scan does not know.</returns>
    public static bool SkipImmediates(byte op, ReadOnlySpan<byte> body, ref int position)
    {
        switch (op)
        {
            case 0x00: // unreachable
            case 0x01: // nop
            case 0x05: // else
            case 0x0F: // return
            case 0x1A: // drop
            case 0x1B: // select
            case 0xD1: // ref.is_null
                return true;
            case 0x0C: // br
            case 0x0D: // br_if
            case 0x10: // call
            case 0x12: // return_call
            case 0x23: // global.get
            case 0x24: // global.set
            case 0x25: // table.get
            case 0x26: // table.set
            case 0x3F: // memory.size
            case 0x40: // memory.grow
            case 0xD2: // ref.func
                ReadU32(body, ref position);
                return true;
            case 0x11: // call_indirect
            case 0x13: // return_call_indirect
                ReadU32(body, ref position);
                ReadU32(body, ref position);
                return true;
            case 0x1C: // select with types: their number, then one byte each
                var types = (int)ReadU32(body, ref position);
                position += types;
                return true;
            case 0x41: // i32.const
            case 0x42: // i64.const
            case 0xD0: // ref.null
                ReadS64(body, ref position);
                return true;
            case 0x43: // f32.const
                position += 4;
                return true;
            case 0x44: // f64.const
                position += 8;
                return true;
            case 0xFC:
                return SkipSaturatingAndBulk(body, ref position);
            case 0xFD:
                return SkipVector(body, ref position);
        }

        if (op is >= 0x28 and <= 0x3E)
        {
            SkipMemoryArgument(body, ref position);
            return true;
        }

        // Comparisons, arithmetic, conversions and sign extension: no immediates.
        return op is >= 0x45 and <= 0xC4;
    }

    private static bool SkipSaturatingAndBulk(ReadOnlySpan<byte> body, ref int position)
    {
        var sub = ReadU32(body, ref position);
        switch (sub)
        {
            case <= 7: // saturating conversions
                return true;
            case 9: // data.drop
            case 11: // memory.fill
            case 13: // elem.drop
            case 15: // table.grow
            case 16: // table.size
            case 17: // table.fill
                ReadU32(body, ref position);
                return true;
            case 8: // memory.init
            case 10: // memory.copy
            case 12: // table.init
            case 14: // table.copy
                ReadU32(body, ref position);
                ReadU32(body, ref position);
                return true;
            default:
                return false;
        }
    }

    private static bool SkipVector(ReadOnlySpan<byte> body, ref int position)
    {
        var sub = ReadU32(body, ref position);
        switch (sub)
        {
            case <= 11: // loads and the store
            case 92:
            case 93:
                SkipMemoryArgument(body, ref position);
                return true;
            case 12: // v128.const
            case 13: // i8x16.shuffle
                position += 16;
                return true;
            case >= 21 and <= 34: // lane access
                position++;
                return true;
            case >= 84 and <= 91: // lane loads and stores
                SkipMemoryArgument(body, ref position);
                position++;
                return true;
            default:
                // Everything else of the vector and relaxed vector instructions has no immediates.
                return sub <= 0x113;
        }
    }

    private static void SkipMemoryArgument(ReadOnlySpan<byte> body, ref int position)
    {
        // Bit 6 of the alignment says a memory index follows (a module with one memory never sets it).
        var alignment = ReadU32(body, ref position);
        if ((alignment & 0x40) != 0)
            ReadU32(body, ref position);

        ReadU64(body, ref position);
    }

    /// <summary>Reads an unsigned number of at most 32 bits.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="position">Where the number starts; moved past it.</param>
    /// <returns>The number.</returns>
    /// <summary>
    /// Reads the import section: the type of every function the module imports, in order (they
    /// come first among the functions a call names); tables, memories and globals are passed over.
    /// </summary>
    /// <param name="section">The section's content.</param>
    /// <param name="functionTypes">Where the types of the imported functions are added.</param>
    /// <returns>False for an import of a kind the engine is configured without.</returns>
    public static bool ReadFunctionImports(ReadOnlySpan<byte> section, List<int> functionTypes)
    {
        var position = 0;
        var count = ReadU32(section, ref position);
        for (var index = 0u; index < count; index++)
        {
            // The module's name and the import's: their lengths, then their bytes.
            var moduleName = (int)ReadU32(section, ref position);
            position += moduleName;
            var importName = (int)ReadU32(section, ref position);
            position += importName;

            switch (section[position++])
            {
                case IMPORT_FUNCTION:
                    functionTypes.Add((int)Math.Min(int.MaxValue, ReadU32(section, ref position)));
                    break;
                case IMPORT_TABLE:
                    position++; // the element type: one byte without the proposals that widen it
                    SkipLimits(section, ref position);
                    break;
                case IMPORT_MEMORY:
                    SkipLimits(section, ref position);
                    break;
                case IMPORT_GLOBAL:
                    position += 2; // the value type and whether it may change
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static void SkipLimits(ReadOnlySpan<byte> section, ref int position)
    {
        var flags = section[position++];
        ReadU32(section, ref position);
        if ((flags & 1) != 0)
            ReadU32(section, ref position);
    }

    public static uint ReadU32(ReadOnlySpan<byte> bytes, ref int position)
    {
        return (uint)ReadU64(bytes, ref position);
    }

    /// <summary>Reads an unsigned number.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="position">Where the number starts; moved past it.</param>
    /// <returns>The number.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The number is longer than ten bytes.</exception>
    public static ulong ReadU64(ReadOnlySpan<byte> bytes, ref int position)
    {
        ulong value = 0;
        for (var shift = 0; shift < 70; shift += 7)
        {
            var part = bytes[position++];
            if (shift < 64)
                value |= (ulong)(part & 0x7F) << shift;

            if ((part & 0x80) == 0)
                return value;
        }

        throw new ArgumentOutOfRangeException(nameof(bytes), "a number of the module is longer than ten bytes");
    }

    /// <summary>Reads a signed number.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="position">Where the number starts; moved past it.</param>
    /// <returns>The number.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The number is longer than ten bytes.</exception>
    public static long ReadS64(ReadOnlySpan<byte> bytes, ref int position)
    {
        long value = 0;
        for (var shift = 0; shift < 70; shift += 7)
        {
            var part = bytes[position++];
            if (shift < 64)
                value |= (long)(part & 0x7F) << shift;

            if ((part & 0x80) != 0)
                continue;

            if (shift < 57 && (part & 0x40) != 0)
                value |= -1L << (shift + 7);

            return value;
        }

        throw new ArgumentOutOfRangeException(nameof(bytes), "a number of the module is longer than ten bytes");
    }

    #endregion
}
