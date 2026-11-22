using System.Text;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The exact names a guest may reach in one of its folders: every file by its path relative to the
/// folder, byte for byte, and every folder those paths go through. A lookup that is not in the
/// table is "no such file" on every node, whatever the node's own file system would have made of
/// it: a name in another case (Windows and macOS find it, Linux does not), a short name, an
/// alternate data stream, a backslash, a composed and a decomposed spelling of one name.
/// </summary>
internal sealed class WasmHostNames
{
    #region Constants

    private const char SEPARATOR = '/';
    private const string HERE = ".";
    private const string UP = "..";

    #endregion

    #region Fields

    private readonly HashSet<string> m_files;
    private readonly HashSet<string> m_folders;

    #endregion

    #region Constructors

    /// <summary>
    /// A table of files. The folders are the ones the files' paths go through, and the folder
    /// itself (the empty path).
    /// </summary>
    /// <param name="files">The files, each by its path relative to the folder, with forward slashes.</param>
    public WasmHostNames(IEnumerable<string> files)
    {
        m_files = new HashSet<string>(StringComparer.Ordinal);
        m_folders = new HashSet<string>(StringComparer.Ordinal) { "" };

        foreach (var file in files)
        {
            m_files.Add(file);

            for (var cut = file.LastIndexOf(SEPARATOR); cut > 0; cut = file.LastIndexOf(SEPARATOR, cut - 1))
                m_folders.Add(file[..cut]);
        }
    }

    #endregion

    #region Functions

    /// <summary>
    /// The table of a folder as it is on disk: every file under it. Meant for a folder whose
    /// content is pinned (a runtime's library tree), read once.
    /// </summary>
    /// <param name="folder">The host folder.</param>
    /// <returns>The table.</returns>
    public static WasmHostNames OfFolder(string folder)
    {
        var root = Path.GetFullPath(folder);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, SEPARATOR));

        return new WasmHostNames(files);
    }

    /// <summary>
    /// Makes one relative path of a folder's path and a path the guest gave from it: "." and empty
    /// segments dropped, ".." taken back.
    /// </summary>
    /// <param name="folder">The folder's own path, already made so ("" for the root).</param>
    /// <param name="path">The guest's path bytes as text.</param>
    /// <param name="joined">The path, relative to the root, without a leading or trailing slash.</param>
    /// <returns>False when the path is absolute or leaves the root.</returns>
    public static bool TryJoin(string folder, string path, out string joined)
    {
        joined = "";
        if (path.Length > 0 && path[0] == SEPARATOR)
            return false;

        var segments = new List<string>();
        if (folder.Length > 0)
            segments.AddRange(folder.Split(SEPARATOR));

        foreach (var segment in path.Split(SEPARATOR))
        {
            if (segment.Length == 0 || segment == HERE)
                continue;

            if (segment == UP)
            {
                if (segments.Count == 0)
                    return false;

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        joined = string.Join(SEPARATOR, segments);
        return true;
    }

    /// <summary>
    /// The number a file or a folder has in the guest's eyes: a function of its guest path alone
    /// (FNV-1a, 64 bits, of the UTF-8 bytes), so it is the same on every node and says nothing of
    /// the node's own file system.
    /// </summary>
    /// <param name="guestRoot">The path the guest sees the folder under.</param>
    /// <param name="relative">The path relative to that folder.</param>
    /// <returns>The number; never zero.</returns>
    public static ulong NumberOf(string guestRoot, string relative)
    {
        var hash = 14695981039346656037UL;
        foreach (var part in new[] { guestRoot.TrimEnd(SEPARATOR), "/", relative })
        {
            foreach (var value in Encoding.UTF8.GetBytes(part))
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
        }

        return hash == 0 ? 1 : hash;
    }

    /// <summary>Whether the path is a file of the table.</summary>
    /// <param name="relative">A path made by <see cref="TryJoin"/>.</param>
    /// <returns>True for a file.</returns>
    public bool IsFile(string relative)
    {
        return m_files.Contains(relative);
    }

    /// <summary>Whether the path is a folder of the table (the root included).</summary>
    /// <param name="relative">A path made by <see cref="TryJoin"/>.</param>
    /// <returns>True for a folder.</returns>
    public bool IsFolder(string relative)
    {
        return m_folders.Contains(relative);
    }

    #endregion

    #region Properties

    /// <summary>The number of files in the table.</summary>
    public int FileCount => m_files.Count;

    #endregion
}
