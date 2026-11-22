namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>Packs LZW codes most significant bit first, as PDF's LZWDecode reads them.</summary>
public sealed class LzwBitWriter
{
    #region Fields

    private readonly MemoryStream m_output = new();
    private int m_buffer;
    private int m_count;

    #endregion

    #region Functions

    /// <summary>Appends one code.</summary>
    /// <param name="code">The code.</param>
    /// <param name="width">Its width in bits (9 to 12).</param>
    public void Write(int code, int width)
    {
        for (var bit = width - 1; bit >= 0; bit--)
        {
            m_buffer = (m_buffer << 1) | ((code >> bit) & 1);
            m_count++;
            if (m_count == 8)
            {
                m_output.WriteByte((byte)m_buffer);
                m_buffer = 0;
                m_count = 0;
            }
        }
    }

    /// <summary>The packed bytes, the last one padded with zero bits.</summary>
    /// <returns>The bytes.</returns>
    public byte[] ToArray()
    {
        if (m_count > 0)
        {
            m_output.WriteByte((byte)(m_buffer << (8 - m_count)));
            m_buffer = 0;
            m_count = 0;
        }

        return m_output.ToArray();
    }

    #endregion
}
