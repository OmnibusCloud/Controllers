using System.Runtime.InteropServices;
using System.Text;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// Where a run may live: a scope of the temp folder the host hands the
/// controller (the client's controllers' temp folder), never a folder of the
/// controller's own. OpenFOAM's fileName class strips whitespace, so a path
/// with a space in it is opened as a different path and nothing is found; on
/// Windows the 8.3 short form of the path, which Windows keeps for every
/// directory on a volume with short names enabled, has no space. Every scratch
/// the controller makes - a case run's, the benchmark's - comes from here, so
/// the rule holds for both.
/// </summary>
public static class FoamScratchPath
{
    #region Functions

    /// <summary>
    /// A fresh private scratch in the host's temp folder, at a path without a
    /// space, with the <c>home</c> and <c>tmp</c> directories the kit's
    /// environment points into.
    /// </summary>
    /// <param name="tempStorage">The host's temp folder.</param>
    /// <param name="label">The scope's label under the temp folder (<c>openfoam</c>, <c>openfoam-benchmark</c>).</param>
    /// <returns>The scratch directory, created.</returns>
    /// <exception cref="InvalidOperationException">The temp folder's path contains a space and has no space-free form.</exception>
    public static string CreateScratch(IWitTempStorage tempStorage, string label)
    {
        var scope = tempStorage.CreateScope(label);

        var scratch = WithoutSpaces(scope);
        if (scratch == null)
        {
            tempStorage.DeleteScope(scope);
            throw new InvalidOperationException(
                $"The temp folder '{tempStorage.RootPath}' has a space in its path and no short form, and OpenFOAM cannot run under it. " +
                "Choose a temp folder without spaces in the client's Settings (Storage).");
        }

        Directory.CreateDirectory(Path.Combine(scratch, "home"));
        Directory.CreateDirectory(Path.Combine(scratch, "tmp"));
        return scratch;
    }

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
