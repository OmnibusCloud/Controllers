namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The bytes a run's <c>random_get</c> gives: a stream that follows from the run's seed alone
/// (xorshift64*), the same on every node. Quality is beside the point; reproducibility is the
/// point. Every seed has a stream of its own: seed 0 keeps the stream it always had, and any
/// other seed starts from its own mixed value (splitmix64), so two seeds that differ in one bit
/// share nothing. (The old rule set the seed's lowest bit, which gave seeds 2k and 2k+1 one
/// stream.) Of the 2^64 seeds two share a stream with another, which no rule over a 64-bit state
/// that must not be zero can avoid.
/// </summary>
internal sealed class WasmHostRandom
{
    #region Constants

    private const ulong UNSEEDED_STATE = 1;

    #endregion

    #region Fields

    private ulong m_state;

    #endregion

    #region Constructors

    /// <summary>
    /// The stream of a seed.
    /// </summary>
    /// <param name="seed">The run's seed.</param>
    public WasmHostRandom(long seed)
    {
        m_state = StateOf(seed);
    }

    #endregion

    #region Functions

    /// <summary>
    /// The state a seed's stream starts from; never zero.
    /// </summary>
    /// <param name="seed">The run's seed.</param>
    /// <returns>The state.</returns>
    public static ulong StateOf(long seed)
    {
        if (seed == 0)
            return UNSEEDED_STATE;

        // splitmix64: a bijection of the 64-bit values, so two seeds never meet here.
        var mixed = unchecked((ulong)seed + 0x9E3779B97F4A7C15UL);
        mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
        mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
        mixed ^= mixed >> 31;

        return mixed == 0 ? UNSEEDED_STATE : mixed;
    }

    /// <summary>Fills a buffer with the next bytes of the stream.</summary>
    /// <param name="buffer">The buffer.</param>
    public void Fill(Span<byte> buffer)
    {
        for (var index = 0; index < buffer.Length; index++)
            buffer[index] = NextByte();
    }

    private byte NextByte()
    {
        m_state ^= m_state >> 12;
        m_state ^= m_state << 25;
        m_state ^= m_state >> 27;
        return (byte)((m_state * 0x2545F4914F6CDD1DUL) >> 56);
    }

    #endregion
}
