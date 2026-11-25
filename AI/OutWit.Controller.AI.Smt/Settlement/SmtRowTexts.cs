using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Solver;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// The texts of a node's row held to what a session writes, before anything is made of them (the
/// row's digest builds its whole canonical form in memory):
/// <list type="bullet">
/// <item>the models and the values are parts of the solver's output, a character of them at least
/// a byte of it, and a session's output ends at its cap
/// (<see cref="SmtProfile.OUTPUT_LIMIT_BYTES"/>): together they have no more characters than
/// that;</item>
/// <item>the message is cut to a row's length (<see cref="SmtResultWords.MAX_MESSAGE_CHARACTERS"/>),
/// the solver's own word for an unknown to the length a session keeps of an answer
/// (<see cref="SmtAnswers.MESSAGE_CHARACTERS"/>), the transcript to its end
/// (<see cref="SmtTranscript.LIMIT_BYTES"/> bytes of UTF-8);</item>
/// <item>every one of them is text: a session reads the solver's output through a decoder that
/// never gives half of a surrogate pair alone, and cuts no text inside a pair. A payload can
/// carry such a half, and the row that holds it is no row of a session.</item>
/// </list>
/// The other texts of a row are held to words the host knows: the problem's id and hash, the
/// ticket, the version, the platform, the words of a result and the code of its status.
/// </summary>
internal static class SmtRowTexts
{
    #region Constants

    private const char FIRST_SURROGATE = (char)0xD800;
    private const char LAST_SURROGATE = (char)0xDFFF;

    #endregion

    #region Functions

    /// <summary>Whether the texts of a row are there, within what a session writes, and text.</summary>
    /// <param name="row">The row.</param>
    /// <returns>True when they are.</returns>
    public static bool Hold(SmtResultData row)
    {
        if (row.Models == null || row.Values == null
            || row.Message is not { Length: <= SmtResultWords.MAX_MESSAGE_CHARACTERS }
            || row.ReasonText is not { Length: <= SmtAnswers.MESSAGE_CHARACTERS }
            || row.Transcript is not { Length: <= SmtTranscript.LIMIT_BYTES })
            return false;

        // The lengths first: nothing longer than a session's output is looked into.
        var models = Characters(row.Models);
        var values = Characters(row.Values);
        if (models < 0 || values < 0 || models + values > SmtProfile.OUTPUT_LIMIT_BYTES)
            return false;

        if (!IsText(row.Message) || !IsText(row.ReasonText) || !IsText(row.Transcript) || !row.Models.TrueForAll(IsText) || !row.Values.TrueForAll(IsText))
            return false;

        // A character of the transcript is a byte at the least, so its characters were within the bound of its bytes.
        return Encoding.UTF8.GetByteCount(row.Transcript) <= SmtTranscript.LIMIT_BYTES;
    }

    /// <summary>Whether a text is valid UTF-16: every half of a surrogate pair stands with its other half, in order.</summary>
    /// <param name="text">The text.</param>
    /// <returns>True for text.</returns>
    public static bool IsText(string text)
    {
        var index = text.AsSpan().IndexOfAnyInRange(FIRST_SURROGATE, LAST_SURROGATE);
        if (index < 0)
            return true;

        for (; index < text.Length; index++)
        {
            if (!char.IsSurrogate(text[index]))
                continue;

            if (!char.IsHighSurrogate(text[index]) || index + 1 == text.Length || !char.IsLowSurrogate(text[index + 1]))
                return false;

            index++;
        }

        return true;
    }

    #endregion

    #region Tools

    /// <summary>The characters of a list of texts in all; below zero when one of them is not there.</summary>
    private static long Characters(List<string> texts)
    {
        long characters = 0;
        foreach (var text in texts)
        {
            if (text == null)
                return -1;

            characters += text.Length;
        }

        return characters;
    }

    #endregion
}
