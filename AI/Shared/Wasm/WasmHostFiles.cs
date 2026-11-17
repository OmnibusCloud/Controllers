using System.Buffers.Binary;
using System.Text;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// What a run's descriptors stand for, so that the host can answer for a path the same way on
/// every node: each folder the guest was given, and every file and folder it opened in them, by
/// the path relative to its folder. A lookup goes through here before WASI sees it: in a folder
/// with a table of names (<see cref="WasmHostNames"/>) a name outside the table does not exist,
/// and what a file's record says of the host (its times, its file and device numbers, a folder's
/// size) is replaced by values that follow from the guest's path alone.
/// </summary>
internal sealed class WasmHostFiles
{
    #region Constants

    /// <summary>The first descriptor WASI gives a preopened folder.</summary>
    public const int FIRST_FOLDER = 3;

    /// <summary>The size of WASI's file record.</summary>
    public const int FILESTAT_SIZE = 64;

    private const int FILESTAT_DEVICE = 0;
    private const int FILESTAT_NUMBER = 8;
    private const int FILESTAT_TYPE = 16;
    private const int FILESTAT_LINKS = 24;
    private const int FILESTAT_LENGTH = 32;
    private const int FILESTAT_TIMES = 40;
    private const byte TYPE_FOLDER = 3;

    private static readonly UTF8Encoding STRICT_UTF8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    #endregion

    #region Fields

    private readonly IReadOnlyList<WasmHostPreopen> m_folders;
    private readonly Dictionary<int, Entry> m_descriptors = new();

    #endregion

    #region Constructors

    /// <summary>
    /// The descriptors of a run that has not started: one a folder, in order.
    /// </summary>
    /// <param name="folders">The folders the guest sees.</param>
    /// <exception cref="ArgumentException">Some folders have a table of names and some have none.</exception>
    public WasmHostFiles(IReadOnlyList<WasmHostPreopen> folders)
    {
        m_folders = folders;

        var tables = folders.Count(folder => folder.Names != null);
        if (tables != 0 && tables != folders.Count)
            throw new ArgumentException("the folders of one run have a table of names each, or none of them has", nameof(folders));

        HasTables = tables > 0;

        for (var index = 0; index < folders.Count; index++)
            m_descriptors[FIRST_FOLDER + index] = new Entry(index, "", IsFolder: true);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Where a path the guest gave leads, from a descriptor it named.
    /// </summary>
    /// <param name="descriptor">The folder's descriptor.</param>
    /// <param name="path">The path's bytes.</param>
    /// <param name="target">The folder it is in and its path relative to that folder.</param>
    /// <returns>0, or the errno of a descriptor that is no folder or a path that cannot be followed.</returns>
    public int Resolve(int descriptor, ReadOnlySpan<byte> path, out Target target)
    {
        target = default;

        if (!m_descriptors.TryGetValue(descriptor, out var entry))
            return descriptor is >= 0 and < FIRST_FOLDER ? WasmHostErrno.NOTDIR : WasmHostErrno.BADF;

        if (!entry.IsFolder)
            return WasmHostErrno.NOTDIR;

        string text;
        try
        {
            text = STRICT_UTF8.GetString(path);
        }
        catch (ArgumentException)
        {
            return WasmHostErrno.ILSEQ;
        }

        if (text.Contains('\0'))
            return WasmHostErrno.ILSEQ;

        if (!WasmHostNames.TryJoin(entry.Relative, text, out var joined))
            return WasmHostErrno.PERM;

        target = new Target(entry.Root, joined, m_folders[entry.Root]);
        return WasmHostErrno.SUCCESS;
    }

    /// <summary>Notes what a descriptor WASI just opened stands for.</summary>
    /// <param name="descriptor">The new descriptor.</param>
    /// <param name="target">What was opened.</param>
    /// <param name="folder">Whether it is a folder.</param>
    public void Opened(int descriptor, Target target, bool folder)
    {
        m_descriptors[descriptor] = new Entry(target.Index, target.Relative, folder);
    }

    /// <summary>Forgets a descriptor the guest closed.</summary>
    /// <param name="descriptor">The descriptor.</param>
    public void Closed(int descriptor)
    {
        m_descriptors.Remove(descriptor);
    }

    /// <summary>Whether a descriptor is a file or a folder of a writable folder.</summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <returns>True when the guest may change what it stands for.</returns>
    public bool IsWritable(int descriptor)
    {
        return m_descriptors.TryGetValue(descriptor, out var entry) && m_folders[entry.Root].Writable;
    }

    /// <summary>
    /// Whether the target is a folder: by the table where the folder has one, by the host's file
    /// system where it has none.
    /// </summary>
    /// <param name="target">A resolved path.</param>
    /// <returns>True for a folder.</returns>
    public static bool IsFolder(Target target)
    {
        if (target.Preopen.Names is { } names)
            return names.IsFolder(target.Relative);

        return Directory.Exists(Path.Combine(target.Preopen.HostPath, target.Relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    /// Rewrites the record WASI filled for a descriptor: nothing of the host stays in it.
    /// </summary>
    /// <param name="record">The 64 bytes of the record in the guest's memory.</param>
    /// <param name="descriptor">The descriptor the record is of.</param>
    public void Neutralize(Span<byte> record, int descriptor)
    {
        if (m_descriptors.TryGetValue(descriptor, out var entry))
            Neutralize(record, m_folders[entry.Root].GuestPath, entry.Relative);
        else
            Neutralize(record, null, "");
    }

    /// <summary>
    /// Rewrites the record WASI filled for a path: nothing of the host stays in it.
    /// </summary>
    /// <param name="record">The 64 bytes of the record in the guest's memory.</param>
    /// <param name="target">The path the record is of.</param>
    public static void Neutralize(Span<byte> record, Target target)
    {
        Neutralize(record, target.Preopen.GuestPath, target.Relative);
    }

    private static void Neutralize(Span<byte> record, string? guestRoot, string relative)
    {
        // No device, a file number that follows from the guest's path (none for a descriptor
        // that has no path: the standard streams), one link, no times; a folder has no size.
        BinaryPrimitives.WriteUInt64LittleEndian(record[FILESTAT_DEVICE..], 0);
        BinaryPrimitives.WriteUInt64LittleEndian(record[FILESTAT_NUMBER..], guestRoot == null ? 0 : WasmHostNames.NumberOf(guestRoot, relative));

        if (guestRoot != null)
            BinaryPrimitives.WriteUInt64LittleEndian(record[FILESTAT_LINKS..], 1);

        if (record[FILESTAT_TYPE] == TYPE_FOLDER)
            BinaryPrimitives.WriteUInt64LittleEndian(record[FILESTAT_LENGTH..], 0);

        record[FILESTAT_TIMES..FILESTAT_SIZE].Clear();
    }

    #endregion

    #region Properties

    /// <summary>Whether the run's folders have tables of names (all of them, then).</summary>
    public bool HasTables { get; }

    #endregion

    #region Nested Types

    /// <summary>A path the guest gave, resolved: the folder it is in and where in it.</summary>
    /// <param name="Index">The folder's place among the run's folders.</param>
    /// <param name="Relative">The path relative to the folder, "" for the folder itself.</param>
    /// <param name="Preopen">The folder.</param>
    public readonly record struct Target(int Index, string Relative, WasmHostPreopen Preopen);

    private sealed record Entry(int Root, string Relative, bool IsFolder);

    #endregion
}
