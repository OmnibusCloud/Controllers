using System.Collections.Concurrent;
using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// Where the kit may be installed for OpenFOAM to run from it. OpenFOAM's
/// <c>fileName</c> rejects whitespace - with the kit's <c>fileName</c> debug
/// switch at 2 every utility dies on its own executable path when the kit sits
/// under a folder with a space (Linux and macOS; the Windows build accepts a
/// space). On Windows the kit's binaries are not long-path aware: a file past
/// 259 characters cannot be opened, and the kit nests about 110 characters
/// below its root. A kit that is in place but cannot be used is refused with
/// the reason in words, so the node leaves the OpenFOAM pool instead of
/// failing every variant.
/// </summary>
public static class FoamKitPathRules
{
    #region Constants

    /// <summary>The longest path a Windows tool without long-path support can open.</summary>
    public const int MAX_WINDOWS_PATH = 259;

    #endregion

    #region Fields

    /// <summary>The longest relative file path of each kit folder this process has measured.</summary>
    private static readonly ConcurrentDictionary<string, int> DEEPEST = new(StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>
    /// Why the kit at <paramref name="root"/> cannot be run from there, or null
    /// when it can.
    /// </summary>
    /// <param name="root">The kit's full root path.</param>
    /// <param name="isWindows">True on Windows.</param>
    /// <param name="deepestRelativePath">The length of the kit's longest relative file path.</param>
    /// <returns>The reason, or null.</returns>
    public static string? Check(string root, bool isWindows, int deepestRelativePath)
    {
        if (!isWindows && FoamCasePathRules.HasWhitespace(root))
        {
            return $"The OpenFOAM kit is installed at '{root}', a path with a space, and OpenFOAM cannot run from a path with a space. " +
                   "Choose a controllers folder without spaces in the client's Settings (Storage); clients from 2.2.4 on use one by default.";
        }

        var deepest = TrimmedLength(root) + 1 + deepestRelativePath;
        if (isWindows && deepest > MAX_WINDOWS_PATH)
        {
            return $"The OpenFOAM kit is installed at '{root}', where its deepest file would be {deepest} characters long, " +
                   $"past the {MAX_WINDOWS_PATH} Windows tools can open. Choose a shorter controllers folder in the client's Settings (Storage).";
        }

        return null;
    }

    /// <summary>
    /// The length of the longest relative file path under <paramref name="root"/>,
    /// measured once per folder per process.
    /// </summary>
    /// <param name="root">The kit's root.</param>
    /// <returns>The length, 0 for an empty or missing folder.</returns>
    public static int DeepestRelativePath(string root)
    {
        return DEEPEST.GetOrAdd(root, Measure);
    }

    #endregion

    #region Tools

    private static int Measure(string root)
    {
        if (!Directory.Exists(root))
            return 0;

        var rootLength = TrimmedLength(root) + 1;
        var deepest = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            deepest = Math.Max(deepest, file.Length - rootLength);

        return deepest;
    }

    private static int TrimmedLength(string root)
    {
        return root.TrimEnd('/', '\\').Length;
    }

    #endregion
}
