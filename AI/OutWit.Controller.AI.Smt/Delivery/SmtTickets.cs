using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OutWit.Controller.AI.Smt.Delivery;

/// <summary>
/// What binds a row to the task package it answers. A ticket is made with a package: 16 bytes
/// from the system's random source, carried as 32 lower-case hex characters. It travels to the
/// node with the package and comes back in every row and in the result package, and the host
/// accepts a result only under a ticket it gave out. A node knows the tickets of the packages it
/// was given and no other, so it cannot answer for work it never had. The index digest says what
/// a package holds and a result must answer: exactly the package's problems, none more, none less
/// and none twice.
/// </summary>
internal static class SmtTickets
{
    #region Constants

    /// <summary>The bytes of a ticket.</summary>
    public const int TICKET_BYTES = 16;

    /// <summary>The characters of a ticket as it travels.</summary>
    public const int TICKET_LENGTH = 2 * TICKET_BYTES;

    #endregion

    #region Functions

    /// <summary>Makes a ticket.</summary>
    /// <returns>32 lower-case hex characters.</returns>
    public static string New()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TICKET_BYTES));
    }

    /// <summary>Whether a text has the form of a ticket.</summary>
    /// <param name="ticket">The text.</param>
    /// <returns>True for 32 lower-case hex characters.</returns>
    public static bool IsWellFormed(string? ticket)
    {
        return ticket is { Length: TICKET_LENGTH } && ticket.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    /// <summary>
    /// The digest of what a package holds: SHA-256 over its problems' indices in ascending order,
    /// each as four bytes, low byte first. An index given twice is counted twice.
    /// </summary>
    /// <param name="indices">The problems' indices, in any order.</param>
    /// <returns>Lower-case hex.</returns>
    public static string IndexDigest(IEnumerable<int> indices)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> number = stackalloc byte[sizeof(int)];

        foreach (var index in indices.Order())
        {
            BinaryPrimitives.WriteInt32LittleEndian(number, index);
            hash.AppendData(number);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    #endregion
}
