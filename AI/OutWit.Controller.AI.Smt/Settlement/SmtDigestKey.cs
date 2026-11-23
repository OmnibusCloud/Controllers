using System.Buffers.Binary;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// The digest of a row (<see cref="Model.SmtResultDigest"/>) as the host keeps it while it
/// settles a run: the 32 bytes of the SHA-256 in two numbers, not the 64 characters of its text.
/// Copies of a problem are compared by it and by nothing else.
/// </summary>
/// <param name="High">The first 16 bytes, big-endian.</param>
/// <param name="Low">The last 16 bytes, big-endian.</param>
internal readonly record struct SmtDigestKey(UInt128 High, UInt128 Low)
{
    #region Constants

    private const int HEX_LENGTH = 64;
    private const int HALF_BYTES = 16;

    #endregion

    #region Functions

    /// <summary>Reads a digest written as 64 lower-case hex characters.</summary>
    /// <param name="hex">The text.</param>
    /// <param name="key">The digest.</param>
    /// <returns>False when the text is not a digest.</returns>
    public static bool TryParse(string? hex, out SmtDigestKey key)
    {
        key = default;
        if (hex is not { Length: HEX_LENGTH } || hex.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            return false;

        var bytes = Convert.FromHexString(hex);
        key = new SmtDigestKey(BinaryPrimitives.ReadUInt128BigEndian(bytes.AsSpan(0, HALF_BYTES)), BinaryPrimitives.ReadUInt128BigEndian(bytes.AsSpan(HALF_BYTES, HALF_BYTES)));
        return true;
    }

    /// <summary>The digest as 64 lower-case hex characters, as a row writes it.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        Span<byte> bytes = stackalloc byte[2 * HALF_BYTES];
        BinaryPrimitives.WriteUInt128BigEndian(bytes[..HALF_BYTES], High);
        BinaryPrimitives.WriteUInt128BigEndian(bytes[HALF_BYTES..], Low);
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>An order of digests that follows from their bytes alone.</summary>
    /// <param name="other">The other digest.</param>
    /// <returns>Below, at or above zero.</returns>
    public int CompareTo(SmtDigestKey other)
    {
        var high = High.CompareTo(other.High);
        return high != 0 ? high : Low.CompareTo(other.Low);
    }

    #endregion
}
