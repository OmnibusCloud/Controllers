using System.Text;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// Where one of a guest's output streams goes: a buffer in memory or a host file, bounded at the
/// write. A byte past the cap is counted and dropped, never stored; the stream only says that it
/// was cut. The guest's stdout and stderr are the host's own (<see cref="WasmHostSyscalls"/>), so
/// no run can fill a disk or the host's memory through them.
/// </summary>
internal sealed class WasmHostOutput : IDisposable
{
    #region Constants

    private const int FIRST_BUFFER_BYTES = 4096;

    #endregion

    #region Fields

    private readonly long m_cap;
    private readonly FileStream? m_file;
    private byte[] m_buffer = [];
    private long m_kept;
    private bool m_cut;

    #endregion

    #region Constructors

    /// <summary>
    /// A stream kept in memory.
    /// </summary>
    /// <param name="capBytes">The most bytes kept; 0 for no cap, less than 0 for a stream that keeps nothing.</param>
    public WasmHostOutput(long capBytes)
    {
        // What memory can hold in one piece is a cap of its own.
        m_cap = capBytes > 0 ? Math.Min(capBytes, Array.MaxLength) : capBytes < 0 ? 0 : Array.MaxLength;
    }

    /// <summary>
    /// A stream written to a host file (made new).
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="capBytes">The most bytes written; 0 for no cap, less than 0 for a stream that keeps nothing.</param>
    /// <exception cref="IOException">The file cannot be made (a host fault).</exception>
    public WasmHostOutput(string path, long capBytes)
    {
        m_cap = capBytes > 0 ? capBytes : capBytes < 0 ? 0 : long.MaxValue;
        m_file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Takes bytes the guest wrote: as many as the cap still allows are kept, the rest dropped.
    /// </summary>
    /// <param name="bytes">The bytes of one buffer of a write.</param>
    /// <exception cref="IOException">The host file cannot be written (a host fault).</exception>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        var room = m_cap - m_kept;
        var take = (int)Math.Min(bytes.Length, room);
        if (take < bytes.Length)
            m_cut = true;

        if (take <= 0)
            return;

        if (m_file != null)
        {
            m_file.Write(bytes[..take]);
        }
        else
        {
            var needed = m_kept + take;
            if (needed > m_buffer.Length)
            {
                var grown = Math.Max(needed, Math.Max(FIRST_BUFFER_BYTES, m_buffer.Length * 2L));
                Array.Resize(ref m_buffer, (int)Math.Min(grown, m_cap));
            }

            bytes[..take].CopyTo(m_buffer.AsSpan((int)m_kept));
        }

        m_kept += take;
    }

    /// <summary>The kept bytes as UTF-8 text (a stream in memory; empty for a file).</summary>
    /// <returns>The text.</returns>
    public string Text()
    {
        return m_file != null ? "" : Encoding.UTF8.GetString(m_buffer, 0, (int)m_kept);
    }

    #endregion

    #region IDisposable

    /// <summary>Closes the host file, if the stream has one.</summary>
    public void Dispose()
    {
        m_file?.Dispose();
    }

    #endregion

    #region Properties

    /// <summary>Whether the guest wrote more than the cap: at least one byte was dropped.</summary>
    public bool Cut => m_cut;

    /// <summary>The bytes kept.</summary>
    public long Kept => m_kept;

    #endregion
}
