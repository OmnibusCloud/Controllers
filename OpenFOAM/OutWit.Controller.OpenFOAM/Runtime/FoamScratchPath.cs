using System.Runtime.InteropServices;
using System.Text;
using OutWit.Controller.OpenFOAM.Model.Rules;
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
/// the rule holds for both, and every scratch goes back to the temp folder
/// through <see cref="Delete"/>.
/// </summary>
public static class FoamScratchPath
{
    #region Functions

    /// <summary>
    /// A fresh private scratch in the host's temp folder, at a path without
    /// whitespace, with the <c>home</c> and <c>tmp</c> directories the kit's
    /// environment points into. A scratch that cannot be completed is given
    /// back before the exception leaves.
    /// </summary>
    /// <param name="tempStorage">The host's temp folder.</param>
    /// <param name="label">The scope's label under the temp folder (<c>openfoam</c>, <c>openfoam-benchmark</c>).</param>
    /// <returns>The scratch, created.</returns>
    /// <exception cref="InvalidOperationException">The temp folder's path contains whitespace and has no whitespace-free form.</exception>
    public static FoamScratch CreateScratch(IWitTempStorage tempStorage, string label)
    {
        var scope = tempStorage.CreateScope(label);

        try
        {
            var usable = WithoutSpaces(scope)
                ?? throw new InvalidOperationException(
                    $"The temp folder '{tempStorage.RootPath}' has a space in its path and no short form, and OpenFOAM cannot run under it. " +
                    "Choose a temp folder without spaces in the client's Settings (Storage).");

            Directory.CreateDirectory(Path.Combine(usable, "home"));
            Directory.CreateDirectory(Path.Combine(usable, "tmp"));
            return new FoamScratch(scope, usable);
        }
        catch
        {
            tempStorage.DeleteScope(scope);
            throw;
        }
    }

    /// <summary>
    /// Gives a scratch back to the host's temp folder. Best-effort, like the
    /// temp folder's own deletion: a file still held open keeps the scope,
    /// which the client takes when it next starts.
    /// </summary>
    /// <param name="tempStorage">The host's temp folder the scratch came from.</param>
    /// <param name="scratch">The scratch.</param>
    /// <returns>True when the scope is gone.</returns>
    public static bool Delete(IWitTempStorage tempStorage, FoamScratch scratch)
    {
        tempStorage.DeleteScope(scratch.ScopePath);
        return !Directory.Exists(scratch.ScopePath);
    }

    /// <summary>
    /// A form of the path without whitespace: the path itself when it has
    /// none, its 8.3 short form on Windows when that exists, null otherwise.
    /// </summary>
    /// <param name="path">An existing directory.</param>
    /// <returns>A whitespace-free path to the same directory, or null when none can be found.</returns>
    public static string? WithoutSpaces(string path)
    {
        if (!FoamCasePathRules.HasWhitespace(path))
            return path;

        if (!OperatingSystem.IsWindows())
            return null;

        var shortPath = ShortPath(path);
        return shortPath != null && !FoamCasePathRules.HasWhitespace(shortPath) ? shortPath : null;
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
