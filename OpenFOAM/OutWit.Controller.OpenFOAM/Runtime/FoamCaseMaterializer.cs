using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// Builds a variant's case directory from the task: every base file at its
/// relative path (from the node's blob cache, so a sweep fetches the base
/// tree once), the templated files instantiated with the variant's values.
/// A file path that would leave the case, a path with a space (OpenFOAM strips
/// whitespace from paths), a leftover of an earlier run (a log, a
/// <c>postProcessing/</c> or <c>processor*</c> tree, which would pass for this
/// run's), or a token left without a value refuses the variant here, before
/// anything runs.
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

        if (task.Case == null)
        {
            findings.Add("The task carries no case.");
            return findings;
        }

        if (task.Case.BaseFiles.Count == 0)
            findings.Add("The task carries no case files.");

        foreach (var file in task.Case.BaseFiles)
        {
            var finding = FoamCasePathRules.Validate(file.RelativePath);
            if (finding != null)
            {
                findings.Add(finding);
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var target = Path.Combine(root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? root);

            var source = await blobService.GetLocalPathAsync(file.BlobId);

            if (!file.Templated)
            {
                File.Copy(source, target, overwrite: true);
                // The blob cache may keep its files read-only; the case is ours to write and to delete.
                File.SetAttributes(target, FileAttributes.Normal);
                continue;
            }

            // Bytes in, bytes out: only the tokens change, whatever the file's
            // encoding, byte order mark or line endings.
            var content = await File.ReadAllBytesAsync(source, cancellationToken);
            var instantiated = FoamTemplating.Substitute(content, task.Substitutions);
            var leftovers = FoamTemplating.LeftoverTokens(FoamCaseText.FromBytes(instantiated));
            foreach (var token in leftovers)
                findings.Add($"{file.RelativePath}: token {token} has no value in this variant.");

            await File.WriteAllBytesAsync(target, instantiated, cancellationToken);
        }

        return findings;
    }

    #endregion
}
