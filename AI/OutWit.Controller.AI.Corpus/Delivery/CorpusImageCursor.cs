using System.Buffers.Binary;

namespace OutWit.Controller.AI.Corpus.Delivery;

/// <summary>
/// A place in a MemoryPack image that is walked before the decoder sees it. Every head the image
/// states (the member count of an object, the count of a list, the lengths of a string) is read
/// here first and held to the bytes that remain and to a bound of its own, so the decoder is
/// handed only an image in which no number points outside it or asks for more than the type can
/// hold. The walk allocates nothing and never throws: an image that fails a check is unsound
/// from there on, every later step leaves it alone, and <see cref="Done"/> says so at the end.
/// </summary>
internal ref struct CorpusImageCursor
{
    #region Constants

    /// <summary>The head of a null string and of a null list.</summary>
    private const int NULL_COLLECTION = -1;

    /// <summary>The head of a null object.</summary>
    private const byte NULL_OBJECT = 255;

    /// <summary>The most bytes a character of a string takes in UTF-8, counted in UTF-16 units: three for a unit of the basic plane, four for the two units of a pair.</summary>
    private const int MAX_UTF8_BYTES_PER_CHAR = 3;

    #endregion

    #region Fields

    private ReadOnlySpan<byte> m_rest;
    private bool m_sound;

    #endregion

    #region Constructors

    /// <summary>Starts a walk at the image's first byte.</summary>
    /// <param name="image">The image, whole.</param>
    public CorpusImageCursor(ReadOnlySpan<byte> image)
    {
        m_rest = image;
        m_sound = true;
    }

    #endregion

    #region Functions

    /// <summary>Passes a value of a fixed size: a number, an identifier, an enumeration.</summary>
    /// <param name="bytes">The value's size.</param>
    public void Skip(int bytes)
    {
        if (!m_sound || bytes < 0 || m_rest.Length < bytes)
        {
            m_sound = false;
            return;
        }

        m_rest = m_rest[bytes..];
    }

    /// <summary>Reads the head of an object: a null, or the count of its members.</summary>
    /// <param name="members">The members the type has: an object written with any other count is not this version's.</param>
    /// <returns>True when an object with exactly these members follows; false for a null object, and for an image that is unsound.</returns>
    public bool Object(int members)
    {
        if (!m_sound || m_rest.IsEmpty)
        {
            m_sound = false;
            return false;
        }

        var head = m_rest[0];
        m_rest = m_rest[1..];

        if (head == NULL_OBJECT)
            return false;

        if (head != members)
            m_sound = false;

        return m_sound;
    }

    /// <summary>
    /// Passes a string: a null, an empty one, UTF-16 (the count of its characters, then two bytes
    /// each) or UTF-8 (the complement of its byte count, the count of its characters, then the
    /// bytes). Both counts are held to the bound and to the bytes that remain, and to one another:
    /// a character is one to three bytes of UTF-8.
    /// </summary>
    /// <param name="maxChars">The most characters the string may have.</param>
    public void Text(int maxChars)
    {
        var head = Head();
        if (!m_sound || head == NULL_COLLECTION || head == 0)
            return;

        if (head > 0)
        {
            if (head > maxChars)
            {
                m_sound = false;
                return;
            }

            Skip(head * sizeof(char));
            return;
        }

        // The complement of a negative head is never negative; it is the number of bytes.
        var bytes = ~head;
        var chars = Head();
        if (!m_sound || chars < 1 || chars > maxChars || bytes < chars || bytes > (long)chars * MAX_UTF8_BYTES_PER_CHAR)
        {
            m_sound = false;
            return;
        }

        Skip(bytes);
    }

    /// <summary>Reads the head of a list: a null, or the count of its items.</summary>
    /// <param name="maxItems">The most items the list may have.</param>
    /// <returns>The items that follow; 0 for a null list, an empty one, and an image that is unsound.</returns>
    public int List(int maxItems)
    {
        var head = Head();
        if (!m_sound || head == NULL_COLLECTION)
            return 0;

        if (head < 0 || head > maxItems)
        {
            m_sound = false;
            return 0;
        }

        return head;
    }

    #endregion

    #region Tools

    /// <summary>Reads a four-byte head; 0 when the image has no four bytes left.</summary>
    private int Head()
    {
        if (!m_sound || m_rest.Length < sizeof(int))
        {
            m_sound = false;
            return 0;
        }

        var head = BinaryPrimitives.ReadInt32LittleEndian(m_rest);
        m_rest = m_rest[sizeof(int)..];
        return head;
    }

    #endregion

    #region Properties

    /// <summary>Whether every head read so far was within its bounds.</summary>
    public bool Sound => m_sound;

    /// <summary>Whether the walk reached the image's last byte with every head within its bounds: nothing is left over.</summary>
    public bool Done => m_sound && m_rest.IsEmpty;

    #endregion
}
