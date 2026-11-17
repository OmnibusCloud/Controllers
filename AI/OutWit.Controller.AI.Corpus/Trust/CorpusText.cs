namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// The shapes a string a node (or a lab) sends may have before the server uses it: every test is
/// over ASCII and bounded by a length, so a string that passes can be written to a document as
/// it is.
/// </summary>
public static class CorpusText
{
    #region Functions

    /// <summary>Whether a string is exactly <paramref name="length"/> lower-case hex characters.</summary>
    /// <param name="text">The string; may be null.</param>
    /// <param name="length">The length it must have.</param>
    /// <returns>True when it is.</returns>
    public static bool IsLowerHex(string? text, int length)
    {
        if (text == null || text.Length != length)
            return false;

        foreach (var c in text)
        {
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                return false;
        }

        return true;
    }

    /// <summary>Whether a string is a lower-case hex SHA-256, or empty.</summary>
    /// <param name="text">The string; may be null.</param>
    /// <returns>True for "" and for 64 lower-case hex characters.</returns>
    public static bool IsSha256OrEmpty(string? text)
    {
        return text != null && (text.Length == 0 || IsLowerHex(text, 64));
    }

    /// <summary>Whether a string is a code: lower-case ASCII letters, digits and underscores, within a length.</summary>
    /// <param name="text">The string; may be null.</param>
    /// <param name="maxLength">The most characters it may have.</param>
    /// <param name="allowEmpty">Whether "" is a code.</param>
    /// <returns>True when it is.</returns>
    public static bool IsCode(string? text, int maxLength, bool allowEmpty = false)
    {
        if (text == null || text.Length > maxLength || (text.Length == 0 && !allowEmpty))
            return false;

        foreach (var c in text)
        {
            if (c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_'))
                return false;
        }

        return true;
    }

    /// <summary>Whether a string is a name of letters, digits and the given marks of ASCII, within a length.</summary>
    /// <param name="text">The string; may be null.</param>
    /// <param name="maxLength">The most characters it may have.</param>
    /// <param name="marks">The characters allowed beside ASCII letters and digits.</param>
    /// <param name="allowEmpty">Whether "" is a name.</param>
    /// <returns>True when it is.</returns>
    public static bool IsName(string? text, int maxLength, string marks, bool allowEmpty = false)
    {
        if (text == null || text.Length > maxLength || (text.Length == 0 && !allowEmpty))
            return false;

        foreach (var c in text)
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
                continue;

            if (!marks.Contains(c))
                return false;
        }

        return true;
    }

    #endregion
}
