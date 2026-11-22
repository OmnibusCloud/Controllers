using System.Text;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// A conversation of a test: it keeps what it was shown at every turn, as text, and answers by a
/// rule of the test's. One conversation serves one run.
/// </summary>
internal sealed class WasmHostConversationScripted : IWasmHostConversation
{
    #region Fields

    private readonly Func<int, string, string?> m_answer;
    private readonly List<string> m_seen = [];

    #endregion

    #region Constructors

    /// <summary>
    /// A conversation that answers by a rule.
    /// </summary>
    /// <param name="answer">The rule: the turn (from 0) and what the guest wrote since the turn before, to the next input; null or empty for the end of input.</param>
    public WasmHostConversationScripted(Func<int, string, string?> answer)
    {
        m_answer = answer;
    }

    #endregion

    #region Functions

    /// <summary>
    /// A conversation that gives fixed pieces of input, one a turn, and then the end of input.
    /// </summary>
    /// <param name="pieces">The pieces, in order.</param>
    /// <returns>The conversation.</returns>
    public static WasmHostConversationScripted Pieces(params string[] pieces)
    {
        return new WasmHostConversationScripted((turn, _) => turn < pieces.Length ? pieces[turn] : null);
    }

    #endregion

    #region IWasmHostConversation

    /// <summary>Keeps what the guest wrote and answers by the rule.</summary>
    /// <param name="output">What the guest wrote to stdout since the previous turn.</param>
    /// <returns>The next input as UTF-8; empty for the end of input.</returns>
    public ReadOnlyMemory<byte> Next(ReadOnlySpan<byte> output)
    {
        var text = Encoding.UTF8.GetString(output);
        m_seen.Add(text);

        var answer = m_answer(m_seen.Count - 1, text);
        return string.IsNullOrEmpty(answer) ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(answer);
    }

    #endregion

    #region Properties

    /// <summary>What the conversation was shown, turn by turn: one entry every time it was asked.</summary>
    public IReadOnlyList<string> Seen => m_seen;

    #endregion
}
