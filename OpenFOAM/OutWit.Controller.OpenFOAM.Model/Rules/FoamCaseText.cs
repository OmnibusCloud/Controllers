using System.Text;

namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// How a case file is read as text without losing a byte: through a one-byte
/// view (ISO-8859-1), in which every byte is one character and every
/// character one byte. Offsets in the text are offsets in the file, and
/// writing the text back gives the file's bytes whatever they were - a byte
/// order mark, a comment in a legacy code page, a sequence that is not UTF-8.
/// The dictionary grammar is ASCII, so tokens, keywords and numbers read the
/// same as in any other view. The node, the Sweep host and the initiator read
/// case files this way and nothing else.
/// </summary>
public static class FoamCaseText
{
    #region Constants

    /// <summary>ISO-8859-1 that refuses, rather than replaces, a character beyond one byte.</summary>
    private static readonly Encoding ONE_BYTE = Encoding.GetEncoding(
        "iso-8859-1",
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    #endregion

    #region Functions

    /// <summary>
    /// The one-byte view of a file's bytes.
    /// </summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <returns>A text with one character per byte.</returns>
    public static string FromBytes(byte[] bytes)
    {
        return ONE_BYTE.GetString(bytes);
    }

    /// <summary>
    /// The bytes of a one-byte view.
    /// </summary>
    /// <param name="text">A text made by <see cref="FromBytes"/> and edited with <see cref="FromValue"/> values.</param>
    /// <returns>The bytes, one per character.</returns>
    /// <exception cref="EncoderFallbackException">The text holds a character beyond one byte - it was not made in the view.</exception>
    public static byte[] ToBytes(string text)
    {
        return ONE_BYTE.GetBytes(text);
    }

    /// <summary>
    /// A value as it enters the view: its UTF-8 bytes, one character each. An
    /// ASCII value (every number, every OpenFOAM word) is itself.
    /// </summary>
    /// <param name="value">The value as the user typed it.</param>
    /// <returns>The value's one-byte view.</returns>
    public static string FromValue(string value)
    {
        return ONE_BYTE.GetString(Encoding.UTF8.GetBytes(value));
    }

    #endregion
}
