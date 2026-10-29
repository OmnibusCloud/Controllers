using OutWit.Controller.OpenFOAM.Extraction;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// The node's side of <see cref="FoamResponseReferences"/>: the case's
/// function objects read twice, from the task's base files as the study
/// templated them (tokens in place, from the node's blob cache) and from
/// the materialised case as the variant has them, so that a force response
/// re-measuring one of the case's own follows every reference value the
/// study sweeps. A case without a controlDict, or one whose files cannot be
/// read, follows nothing and the request is written as it came.
/// </summary>
public static class FoamResponseFollowing
{
    #region Constants

    private const string CONTROL_DICT = "system/controlDict";

    #endregion

    #region Functions

    /// <summary>
    /// The request to write for a variant: the task's, with the swept
    /// reference values of every re-measured force taken from the case.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="caseDirectory">The materialised case.</param>
    /// <param name="blobService">The node's blob service, for the base files.</param>
    /// <param name="notes">
    /// Receives one <see cref="FoamNoteKind.Info"/> per parameter that
    /// follows: the values stand, measured with the variant's reference
    /// value; the note is about the whole response, so it names no column.
    /// </param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The request to write; the task's own when nothing follows.</returns>
    public static async Task<FoamExtractionRequestData?> FollowAsync(FoamTaskData task, string caseDirectory, IWitBlobService blobService, ICollection<FoamNote> notes, CancellationToken cancellationToken = default)
    {
        var request = task.Case?.Extraction;
        if (request == null || task.Case == null || !request.Responses.Any(response => response.Kind is FoamResponseKind.Forces or FoamResponseKind.ForceCoeffs))
            return request;

        var templated = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in task.Case.BaseFiles.Where(file => file.RelativePath == CONTROL_DICT || file.RelativePath.StartsWith("system/", StringComparison.Ordinal) || !file.RelativePath.Contains('/')))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await ReadBlobAsync(blobService, file, cancellationToken);
            if (text != null)
                templated[file.RelativePath] = text;
        }

        if (!templated.TryGetValue(CONTROL_DICT, out var templatedControlDict))
            return request;

        var root = Path.GetFullPath(caseDirectory);
        var instantiatedControlDict = ReadCaseFile(root, CONTROL_DICT);
        if (instantiatedControlDict == null)
            return request;

        var sentences = new List<string>();
        var followed = FoamResponseReferences.Follow(
            request,
            FoamCaseFunctionObjects.Enumerate(templatedControlDict, path => templated.GetValueOrDefault(path)),
            templatedControlDict,
            FoamCaseFunctionObjects.Enumerate(instantiatedControlDict, path => ReadCaseFile(root, path)),
            instantiatedControlDict,
            sentences);

        foreach (var sentence in sentences)
            notes.Add(new FoamNote { Kind = FoamNoteKind.Info, Text = sentence });

        return followed;
    }

    #endregion

    #region Tools

    private static async Task<string?> ReadBlobAsync(IWitBlobService blobService, FoamFileRefData file, CancellationToken cancellationToken)
    {
        try
        {
            var path = await blobService.GetLocalPathAsync(file.BlobId);
            return FoamCaseText.FromBytes(await File.ReadAllBytesAsync(path, cancellationToken));
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

    private static string? ReadCaseFile(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!Path.GetFullPath(path).StartsWith(root, StringComparison.Ordinal) || !File.Exists(path))
            return null;

        try
        {
            return FoamCaseText.FromBytes(File.ReadAllBytes(path));
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
