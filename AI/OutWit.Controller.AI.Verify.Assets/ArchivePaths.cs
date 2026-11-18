namespace OutWit.Controller.AI.Verify.Assets;

/// <summary>
/// Where an archive's entry may be written: under the folder it is unpacked into, and nowhere
/// else. Every archive this tool unpacks is pinned by its SHA-256, so an entry that names a
/// place outside its folder ("../", an absolute path, a drive) is not expected; the check is
/// here so that a pin moved to a bad archive writes nothing outside the build's own folders
/// before anyone has looked at what the archive holds.
/// </summary>
internal static class ArchivePaths
{
    #region Constants

    private const char SEPARATOR = '/';
    private const char DRIVE_MARK = ':';

    #endregion

    #region Functions

    /// <summary>
    /// The path under <paramref name="root"/> an entry is written to.
    /// </summary>
    /// <param name="root">The folder the archive is unpacked into.</param>
    /// <param name="entryName">The entry's name inside the archive, or the part of it that is kept.</param>
    /// <returns>The full path of the file to write.</returns>
    /// <exception cref="InvalidDataException">The name is empty, absolute, or has a segment that is empty, "." or "..", or a drive mark.</exception>
    public static string Under(string root, string entryName)
    {
        var name = entryName.Replace('\\', SEPARATOR);
        var segments = name.Split(SEPARATOR);

        if (name.Length == 0 || name.Contains(DRIVE_MARK) || segments.Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException($"the archive holds an entry that is not a plain relative path: '{entryName}'");

        var fullRoot = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(fullRoot, Path.Combine(segments)));

        // The segments are plain, so this cannot fail; it is the property itself, stated once more.
        if (!path.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"the archive holds an entry outside its folder: '{entryName}'");

        return path;
    }

    #endregion
}
