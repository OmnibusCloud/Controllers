using System.Runtime.InteropServices;
using System.Text;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// Where a run may live. OpenFOAM's fileName class strips whitespace, so a
/// path with a space in it is opened as a different path and nothing is
/// found (plan D-16). The node's temp directory is under the user's profile
/// on Windows - <c>C:\Users\John Smith\AppData\Local\Temp</c> is the common
/// case - and there the 8.3 short form of the path, which Windows keeps for
/// every directory on a volume with short names enabled, has no space.
/// </summary>
public static class FoamScratchPath
{
    #region Functions

    /// <summary>
    /// A form of the path without a space: the path itself when it has none,
    /// its 8.3 short form on Windows when that exists, null otherwise.
    /// </summary>
    /// <param name="path">An existing directory.</param>
    /// <returns>A space-free path to the same directory, or null when none can be found.</returns>
    public static string? WithoutSpaces(string path)
    {
        if (!path.Contains(' '))
            return path;

        if (!OperatingSystem.IsWindows())
            return null;

        var shortPath = ShortPath(path);
        return shortPath != null && !shortPath.Contains(' ') ? shortPath : null;
    }

    /// <summary>
    /// The Windows 8.3 short form of an existing path.
    /// </summary>
    /// <param name="path">An existing path.</param>
    /// <returns>The short form, or null when Windows has none for it.</returns>
    public static string? ShortPath(string path)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var buffer = new StringBuilder(1024);
        var length = GetShortPathName(path, buffer, buffer.Capacity);
        if (length == 0 || length > buffer.Capacity)
            return null;

        return buffer.ToString(0, (int)length);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, int bufferSize);

    #endregion
}
