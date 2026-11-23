using System.Text;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// The caller's side of a conversation with the solver that sends one text and listens: the
/// text at the solver's first read, the end of input at the next one, and everything the
/// solver wrote kept as its answer. The tests' own type, with nothing of the controller in its
/// shape.
/// </summary>
internal sealed class SmtRawConversation
{
    #region Fields

    private readonly byte[] m_input;
    private readonly StringBuilder m_answer = new();
    private bool m_sent;

    #endregion

    #region Constructors

    /// <summary>
    /// Starts a conversation.
    /// </summary>
    /// <param name="input">Everything the solver is given.</param>
    public SmtRawConversation(string input)
    {
        m_input = Encoding.UTF8.GetBytes(input);
    }

    #endregion

    #region Functions

    /// <summary>
    /// The solver's next input: the text once, then the end of input.
    /// </summary>
    /// <param name="output">What the solver wrote since its previous read.</param>
    /// <returns>The input, or nothing for the end of it.</returns>
    public ReadOnlyMemory<byte> Next(ReadOnlySpan<byte> output)
    {
        m_answer.Append(Encoding.UTF8.GetString(output));
        if (m_sent)
            return ReadOnlyMemory<byte>.Empty;

        m_sent = true;
        return m_input;
    }

    #endregion

    #region Properties

    /// <summary>Everything the solver wrote.</summary>
    public string Answer => m_answer.ToString();

    #endregion
}
