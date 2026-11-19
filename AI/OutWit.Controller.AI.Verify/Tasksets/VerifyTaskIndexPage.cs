namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// The task indices of one page of <see cref="VerifyTaskIndices"/>: 65,536 neighbouring numbers.
/// A page that holds few of them keeps them as a short sorted list, two bytes an index, and
/// becomes a bitmap (a bit a number, 8 KiB) only when the list would be a quarter of that. So a
/// page costs what the indices in it cost, never the range they are spread over.
/// </summary>
internal sealed class VerifyTaskIndexPage
{
    #region Constants

    /// <summary>The numbers a page covers.</summary>
    public const int SIZE = 1 << 16;

    /// <summary>The most indices a page keeps as a list; one more makes it a bitmap.</summary>
    public const int LIST_LIMIT = 1024;

    private const int FIRST_LIST_LENGTH = 4;
    private const int BITMAP_WORDS = SIZE / 64;

    #endregion

    #region Fields

    private ushort[]? m_list = new ushort[FIRST_LIST_LENGTH];
    private int m_listed;
    private ulong[]? m_bits = new ulong[BITMAP_WORDS];

    #endregion

    #region Functions

    /// <summary>Adds a number of the page.</summary>
    /// <param name="offset">The number within the page, 0 to 65,535.</param>
    /// <returns>False when the page held it already.</returns>
    public bool Add(int offset)
    {
        if (m_bits != null)
            return Set(m_bits, offset);

        var list = m_list!;
        var place = Array.BinarySearch(list, 0, m_listed, (ushort)offset);
        if (place >= 0)
            return false;

        if (m_listed == LIST_LIMIT)
        {
            // The list has grown to where a bitmap is the smaller of the two.
            m_bits = new ulong[BITMAP_WORDS];
            for (var index = 0; index < m_listed; index++)
                Set(m_bits, list[index]);

            m_list = null;
            return Set(m_bits, offset);
        }

        if (m_listed == list.Length)
        {
            Array.Resize(ref list, Math.Min(LIST_LIMIT, list.Length * 2));
            m_list = list;
        }

        place = ~place;
        Array.Copy(list, place, list, place + 1, m_listed - place);
        list[place] = (ushort)offset;
        m_listed++;
        return true;
    }

    private static bool Set(ulong[] bits, int offset)
    {
        var bit = 1UL << (offset & 63);
        if ((bits[offset >> 6] & bit) != 0)
            return false;

        bits[offset >> 6] |= bit;
        return true;
    }

    #endregion
}
