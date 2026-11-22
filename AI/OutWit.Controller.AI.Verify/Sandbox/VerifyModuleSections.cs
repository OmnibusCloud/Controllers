namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Reads the sections of a module that are not code, for what the engine compiles besides the
/// functions' bodies: the types of the imported functions (a call of one counts the values of its
/// type), how many imports and exports there are, and which of the module's own functions leave
/// it (exported, in an element segment, or named by <c>ref.func</c> in a global's value): the
/// engine compiles an entry for every such function. Every count is held to what is left of the
/// section, and a length that does not fit is unreadable (<see cref="ArgumentOutOfRangeException"/>).
/// </summary>
internal static class VerifyModuleSections
{
    #region Constants

    private const byte KIND_FUNCTION = 0x00;
    private const byte KIND_TABLE = 0x01;
    private const byte KIND_MEMORY = 0x02;
    private const byte KIND_GLOBAL = 0x03;

    private const byte OP_END = 0x0B;
    private const byte OP_REF_FUNC = 0xD2;

    #endregion

    #region Functions

    /// <summary>
    /// Reads the import section: the type of every function the module imports, in order (they
    /// come first among the functions a call names); tables, memories and globals are passed over.
    /// </summary>
    /// <param name="section">The section's content.</param>
    /// <param name="functionTypes">Where the types of the imported functions are added.</param>
    /// <param name="imports">The imports of every kind.</param>
    /// <returns>False for an import of a kind the engine is configured without.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A count or a length does not fit the section.</exception>
    public static bool ReadImports(ReadOnlySpan<byte> section, List<int> functionTypes, out long imports)
    {
        var position = 0;
        imports = Count(section, ref position);
        for (var index = 0L; index < imports; index++)
        {
            // The module's name and the import's: their lengths, then their bytes.
            Skip(section, ref position, Length(section, ref position));
            Skip(section, ref position, Length(section, ref position));

            switch (section[position++])
            {
                case KIND_FUNCTION:
                    functionTypes.Add((int)Math.Min(int.MaxValue, VerifyModuleCode.ReadU32(section, ref position)));
                    break;
                case KIND_TABLE:
                    position++; // the element type: one byte without the proposals that widen it
                    SkipLimits(section, ref position);
                    break;
                case KIND_MEMORY:
                    SkipLimits(section, ref position);
                    break;
                case KIND_GLOBAL:
                    position += 2; // the value type and whether it may change
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>Reads the export section: how many exports there are, and which functions they name.</summary>
    /// <param name="section">The section's content.</param>
    /// <param name="escaping">The functions that leave the module, by their index among all functions.</param>
    /// <returns>The exports.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A count or a length does not fit the section.</exception>
    public static long ReadExports(ReadOnlySpan<byte> section, HashSet<uint> escaping)
    {
        var position = 0;
        var exports = Count(section, ref position);
        for (var index = 0L; index < exports; index++)
        {
            Skip(section, ref position, Length(section, ref position));
            var kind = section[position++];
            var item = VerifyModuleCode.ReadU32(section, ref position);
            if (kind == KIND_FUNCTION)
                escaping.Add(item);
        }

        return exports;
    }

    /// <summary>Reads the element section: every function an element segment names leaves the module.</summary>
    /// <param name="section">The section's content.</param>
    /// <param name="escaping">The functions that leave the module, by their index among all functions.</param>
    /// <returns>False for a segment of a form the engine is configured without.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A count or a length does not fit the section.</exception>
    public static bool ReadElements(ReadOnlySpan<byte> section, HashSet<uint> escaping)
    {
        var position = 0;
        var segments = Count(section, ref position);
        for (var index = 0L; index < segments; index++)
        {
            var flags = VerifyModuleCode.ReadU32(section, ref position);
            if (flags > 7)
                return false;

            // Bit 1 with bit 0 clear: a table index; bit 0 clear: an offset; then an element kind or a type.
            if ((flags & 3) == 2)
                VerifyModuleCode.ReadU32(section, ref position);
            if ((flags & 1) == 0)
                ReadConstant(section, ref position, escaping);
            if ((flags & 3) != 0)
                position++;

            var items = Count(section, ref position);
            for (var item = 0L; item < items; item++)
            {
                if ((flags & 4) != 0)
                    ReadConstant(section, ref position, escaping);
                else
                    escaping.Add(VerifyModuleCode.ReadU32(section, ref position));
            }
        }

        return true;
    }

    /// <summary>Reads the global section: a function a global's value names leaves the module.</summary>
    /// <param name="section">The section's content.</param>
    /// <param name="escaping">The functions that leave the module, by their index among all functions.</param>
    /// <exception cref="ArgumentOutOfRangeException">A count or a length does not fit the section.</exception>
    public static void ReadGlobals(ReadOnlySpan<byte> section, HashSet<uint> escaping)
    {
        var position = 0;
        var globals = Count(section, ref position);
        for (var index = 0L; index < globals; index++)
        {
            position += 2; // the value type and whether it may change
            ReadConstant(section, ref position, escaping);
        }
    }

    /// <summary>A count of entries of at least a byte each: no more than what is left of the section.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The count is more than the bytes left.</exception>
    public static long Count(ReadOnlySpan<byte> section, ref int position)
    {
        var count = VerifyModuleCode.ReadU32(section, ref position);
        if (count > (uint)Math.Max(0, section.Length - position))
            throw new ArgumentOutOfRangeException(nameof(section), "a count of the module is more than its section holds");

        return count;
    }

    /// <summary>A length of bytes that follow: no more than what is left of the section.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The length is more than the bytes left.</exception>
    public static int Length(ReadOnlySpan<byte> section, ref int position)
    {
        var length = VerifyModuleCode.ReadU32(section, ref position);
        if (length > (uint)Math.Max(0, section.Length - position))
            throw new ArgumentOutOfRangeException(nameof(section), "a length of the module is more than its section holds");

        return (int)length;
    }

    private static void Skip(ReadOnlySpan<byte> section, ref int position, int bytes)
    {
        position += bytes;
    }

    /// <summary>A constant expression: <c>ref.func</c> in it names a function that leaves the module.</summary>
    private static void ReadConstant(ReadOnlySpan<byte> section, ref int position, HashSet<uint> escaping)
    {
        for (var steps = 0; ; steps++)
        {
            if (steps > section.Length)
                throw new ArgumentOutOfRangeException(nameof(section), "a constant expression of the module does not end");

            var op = section[position++];
            if (op == OP_END)
                return;

            if (op == OP_REF_FUNC)
            {
                escaping.Add(VerifyModuleCode.ReadU32(section, ref position));
                continue;
            }

            if (!VerifyModuleCode.SkipImmediates(op, section, ref position))
                throw new ArgumentOutOfRangeException(nameof(section), "a constant expression of the module cannot be read");
        }
    }

    private static void SkipLimits(ReadOnlySpan<byte> section, ref int position)
    {
        var flags = section[position++];
        VerifyModuleCode.ReadU32(section, ref position);
        if ((flags & 1) != 0)
            VerifyModuleCode.ReadU32(section, ref position);
    }

    #endregion
}
