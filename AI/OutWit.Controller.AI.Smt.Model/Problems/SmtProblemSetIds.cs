using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OutWit.Controller.AI.Smt.Model.Problems;

/// <summary>
/// The ids of a problem set: what an id is, and which rows have taken which. An id names one
/// row of its set; the first row with an id keeps it.
/// <para>
/// A set may have a million rows, so an id is remembered as the first 128 bits of its SHA-256
/// and the line it stood on: the same few bytes whatever the id's length, and no two ids anybody
/// can find share them.
/// </para>
/// </summary>
internal sealed class SmtProblemSetIds
{
    #region Constants

    private const string MARKS = "._-/+:@=";
    private const int HASH_BYTES = 32;

    #endregion

    #region Fields

    private readonly Dictionary<UInt128, int> m_lines = [];

    #endregion

    #region Functions

    /// <summary>
    /// Whether a text is an id: 1 to 64 characters, each an ASCII letter, a digit or one of
    /// <c>. _ - / + : @ =</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>True for an id.</returns>
    public static bool IsId(string text)
    {
        if (text.Length is 0 or > SmtProblemSetLimits.MAX_ID_CHARACTERS)
            return false;

        foreach (var character in text)
        {
            if (!char.IsAsciiLetterOrDigit(character) && !MARKS.Contains(character))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Gives an id to the row on a line, unless an earlier row has it.
    /// </summary>
    /// <param name="id">The id (<see cref="IsId"/>).</param>
    /// <param name="line">The line of the row that asks for it.</param>
    /// <param name="earlier">The line of the row that has the id already; 0 when none had.</param>
    /// <returns>True when the id was free and is the row's now.</returns>
    public bool TryTake(string id, int line, out int earlier)
    {
        Span<byte> text = stackalloc byte[SmtProblemSetLimits.MAX_ID_CHARACTERS];
        Span<byte> hash = stackalloc byte[HASH_BYTES];

        var length = Encoding.ASCII.GetBytes(id, text);
        SHA256.HashData(text[..length], hash);
        var key = BinaryPrimitives.ReadUInt128LittleEndian(hash);

        if (m_lines.TryGetValue(key, out earlier))
            return false;

        m_lines.Add(key, line);
        return true;
    }

    #endregion
}
