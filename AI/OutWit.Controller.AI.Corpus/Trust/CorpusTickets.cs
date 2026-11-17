using System.Security.Cryptography;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// The tickets that bind a result to its task on the server: 16 bytes of the platform's
/// cryptographic random source as 32 lower-case hex characters, one per shard per round. A node
/// learns only the tickets of the tasks it was given, so it cannot return a result for a shard it
/// never had; a ticket is never written to a document a lab sees.
/// </summary>
public static class CorpusTickets
{
    #region Constants

    /// <summary>Random bytes in a ticket: 128 bits.</summary>
    public const int BYTES = 16;

    /// <summary>Characters of a ticket as it travels.</summary>
    public const int LENGTH = BYTES * 2;

    #endregion

    #region Functions

    /// <summary>Makes a new ticket.</summary>
    /// <returns>32 lower-case hex characters.</returns>
    public static string New()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(BYTES));
    }

    /// <summary>Whether a string has the shape of a ticket (it may still be nobody's).</summary>
    /// <param name="ticket">What a task or a result carries; may be null (the wire carries nulls).</param>
    /// <returns>True for exactly 32 lower-case hex characters.</returns>
    public static bool IsWellFormed(string? ticket)
    {
        return CorpusText.IsLowerHex(ticket, LENGTH);
    }

    #endregion
}
