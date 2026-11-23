using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Live.Pack;

/// <summary>
/// The ids of a packed problem set: the file's path relative to the folder, with <c>/</c> between
/// its parts and without the <c>.smt2</c> extension, made a valid id (1 to 64 characters, each a
/// letter, a digit or one of <c>. _ - / + : @ =</c>): every other character becomes <c>_</c>. A
/// path longer than an id keeps its last characters behind the head of the SHA-256 of the whole
/// path and an <c>=</c>; so does a path whose id an earlier file of the set has taken. The same
/// folder gives the same ids on every machine.
/// </summary>
internal sealed class SmtLiveProblemIds
{
    #region Constants

    /// <summary>The extension of an SMT-LIB file.</summary>
    public const string EXTENSION = ".smt2";

    private const string MARKS = "._-/+:@=";
    private const char REPLACEMENT = '_';
    private const char SEPARATOR = '/';
    private const char HASH_SEPARATOR = '=';
    private const int HASH_CHARACTERS = 8;

    #endregion

    #region Fields

    private readonly HashSet<string> m_taken = new(StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>
    /// The id of a file, unique among the ids this instance has given.
    /// </summary>
    /// <param name="relativePath">The file's path relative to the folder.</param>
    /// <returns>The id.</returns>
    public string Of(string relativePath)
    {
        var id = Plain(relativePath);
        if (m_taken.Add(id))
            return id;

        var made = Made(relativePath);
        id = Hashed(relativePath, made);
        for (var attempt = 2; !m_taken.Add(id); attempt++)
            id = Hashed($"{relativePath}#{attempt}", made);

        return id;
    }

    /// <summary>
    /// The id a file's path gives before any other file is considered.
    /// </summary>
    /// <param name="relativePath">The file's path relative to the folder.</param>
    /// <returns>The id.</returns>
    public static string Plain(string relativePath)
    {
        var made = Made(relativePath);
        return made.Length <= SmtProblemSetLimits.MAX_ID_CHARACTERS ? made : Hashed(relativePath, made);
    }

    private static string Made(string relativePath)
    {
        var path = Normal(relativePath);
        if (path.Length > EXTENSION.Length && path.EndsWith(EXTENSION, StringComparison.OrdinalIgnoreCase))
            path = path[..^EXTENSION.Length];

        var id = new StringBuilder(path.Length);
        foreach (var character in path)
            id.Append(char.IsAsciiLetterOrDigit(character) || MARKS.Contains(character) ? character : REPLACEMENT);

        return id.Length == 0 ? REPLACEMENT.ToString() : id.ToString();
    }

    private static string Hashed(string key, string made)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normal(key))))[..HASH_CHARACTERS];
        var room = SmtProblemSetLimits.MAX_ID_CHARACTERS - HASH_CHARACTERS - 1;
        return $"{hash}{HASH_SEPARATOR}{(made.Length <= room ? made : made[^room..])}";
    }

    private static string Normal(string path)
    {
        return path.Replace('\\', SEPARATOR);
    }

    #endregion
}
