using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// The node's side of <see cref="FoamCaseAdvice"/>: the task's data, and
/// the templated files the advice reads - <c>system/setFieldsDict</c> and
/// the initial fields the study templated, tokens in place, from the node's
/// blob cache - given to the Model, the sentences kept for
/// <c>log.responses</c>. A file that cannot be read says nothing.
/// </summary>
public static class FoamCaseAdvisor
{
    #region Constants

    private const string SET_FIELDS_DICT = "system/setFieldsDict";

    private static readonly string[] INITIAL_FOLDERS = ["0/", "0.orig/"];

    #endregion

    #region Functions

    /// <summary>
    /// The advice a task gives.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="blobService">The node's blob service, for the base files.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>Sentences, one each; empty when there is nothing to say.</returns>
    public static async Task<IReadOnlyList<string>> AdviseAsync(FoamTaskData task, IWitBlobService blobService, CancellationToken cancellationToken = default)
    {
        var advice = new List<string>(FoamCaseAdvice.Of(task.Case));
        if (task.Case?.Recipe == null || task.Case.Recipe.Steps.All(step => step.Utility != "setFields"))
            return advice;

        var files = new List<(string RelativePath, string? Text)>();
        foreach (var file in task.Case.BaseFiles)
        {
            var wanted = file.RelativePath == SET_FIELDS_DICT
                         || (file.Templated && INITIAL_FOLDERS.Any(folder => file.RelativePath.StartsWith(folder, StringComparison.Ordinal)));
            if (!wanted || !FoamCaseContentRules.IsScanned(file.RelativePath, file.Size))
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            files.Add((file.RelativePath, await ReadBlobAsync(blobService, file, cancellationToken)));
        }

        advice.AddRange(FoamCaseAdvice.OfSetFields(task.Case.Recipe, files));
        return advice;
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

    #endregion
}
