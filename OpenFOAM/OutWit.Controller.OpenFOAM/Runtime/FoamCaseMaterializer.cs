using System.Text;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Recipes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// Builds a variant's case directory from the task: every base file at its
/// relative path (from the node's blob cache, so a sweep fetches the base
/// tree once), the templated files instantiated with the variant's values.
/// A file path that would leave the case, a path with a space (plan D-16), or
/// a token left without a value refuses the variant here, before anything runs.
/// </summary>
public static class FoamCaseMaterializer
{
    #region Functions

    /// <summary>
    /// Materialises the case.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="caseDirectory">The (existing, empty) case directory.</param>
    /// <param name="blobService">The node's blob service.</param>
    /// <param name="cancellationToken">Cancels the transfers.</param>
    /// <returns>Findings, one sentence each; empty when the case is complete.</returns>
    public static async Task<IReadOnlyList<string>> MaterializeAsync(
        FoamTaskData task,
        string caseDirectory,
        IWitBlobService blobService,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<string>();
        var root = Path.GetFullPath(caseDirectory);

        if (task.BaseFiles.Count == 0)
            findings.Add("The task carries no case files.");

        foreach (var file in task.BaseFiles)
        {
            var finding = ValidatePath(file.RelativePath);
            if (finding != null)
            {
                findings.Add(finding);
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var target = Path.Combine(root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var source = await blobService.GetLocalPathAsync(file.BlobId);

            if (!file.Templated)
            {
                File.Copy(source, target, overwrite: true);
                continue;
            }

            var text = await File.ReadAllTextAsync(source, Encoding.UTF8, cancellationToken);
            var instantiated = FoamTemplating.Substitute(text, task.Substitutions);
            var leftovers = FoamTemplating.LeftoverTokens(instantiated);
            foreach (var token in leftovers)
                findings.Add($"{file.RelativePath}: token {token} has no value in this variant.");

            await File.WriteAllTextAsync(target, instantiated, new UTF8Encoding(false), cancellationToken);
        }

        return findings;
    }

    /// <summary>
    /// Checks a relative path of the base tree: relative, inside the case,
    /// forward slashes, no space, no empty segment.
    /// </summary>
    /// <param name="relativePath">The path as the task carries it.</param>
    /// <returns>A finding, or null when the path is acceptable.</returns>
    public static string? ValidatePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return "A case file has no path.";
        if (relativePath.Contains('\\'))
            return $"{relativePath}: a case file path must use forward slashes.";
        if (relativePath.Contains(' '))
            return $"{relativePath}: a case file path must not contain a space (OpenFOAM strips whitespace from paths).";
        if (FoamRecipeValidator.IsPathEscape(relativePath))
            return $"{relativePath}: a case file path must stay inside the case directory.";
        if (relativePath.Split('/').Any(segment => segment.Length == 0 || segment == "."))
            return $"{relativePath}: a case file path must not have empty or '.' segments.";

        return null;
    }

    #endregion
}
