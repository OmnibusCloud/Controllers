using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Inspection;

/// <summary>
/// The node-side rejects, applied to the materialised case before anything
/// runs: the case read from disk the way <see cref="FoamCaseContentRules"/>
/// expects it (every file by its relative path, the text of the scanned ones
/// in the one-byte view), the verdict the Model's - the same sentences the
/// initiator's preflight prints.
/// </summary>
public static class FoamCaseInspector
{
    #region Functions

    /// <summary>
    /// Inspects a case directory.
    /// </summary>
    /// <param name="caseDirectory">The case root.</param>
    /// <param name="kitHasLibrary">Answers whether a library named in a <c>libs</c> entry is in the kit; null accepts every name.</param>
    /// <returns>Findings, one sentence each with file and line where known; empty when the case may run.</returns>
    public static IReadOnlyList<string> Inspect(string caseDirectory, Func<string, bool>? kitHasLibrary = null)
    {
        var root = Path.GetFullPath(caseDirectory);
        var files = new List<(string RelativePath, string? Text)>();

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            files.Add((relative, ReadScanned(file, relative)));
        }

        return FoamCaseContentRules.Inspect(files, kitHasLibrary);
    }

    private static string? ReadScanned(string file, string relative)
    {
        try
        {
            if (!FoamCaseContentRules.IsScanned(relative, new FileInfo(file).Length))
                return null;

            return FoamCaseText.FromBytes(File.ReadAllBytes(file));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    #endregion
}
