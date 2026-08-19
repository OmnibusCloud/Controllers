using System.Text;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// Thread-safe bounded tail of a process stream: keeps the most recent lines up to a character
/// capacity, dropping whole lines from the head in amortized O(1) as new ones arrive.
/// </summary>
internal sealed class ParaViewProcessOutputTail
{
    #region Fields

    private readonly int m_capacity;

    private readonly Queue<string> m_lines = new();

    private readonly object m_lock = new();

    private int m_length;

    #endregion

    #region Constructors

    public ParaViewProcessOutputTail(int capacity)
    {
        m_capacity = capacity;
    }

    #endregion

    #region Functions

    public void Append(string? line)
    {
        if (line == null)
            return;

        lock (m_lock)
        {
            m_lines.Enqueue(line);
            m_length += line.Length + 1;

            while (m_length > m_capacity && m_lines.Count > 1)
                m_length -= m_lines.Dequeue().Length + 1;
        }
    }

    #endregion

    #region Properties

    public string Text
    {
        get
        {
            lock (m_lock)
            {
                var builder = new StringBuilder(m_length);
                foreach (var line in m_lines)
                    builder.Append(line).Append('\n');

                var text = builder.ToString();
                return text.Length <= m_capacity ? text : text[^m_capacity..];
            }
        }
    }

    #endregion
}
