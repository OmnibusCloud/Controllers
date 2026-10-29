using OutWit.Controller.OpenFOAM.Extraction;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// The node's side of <see cref="FoamCaseAdvice"/>: the task's data, and
/// the files the advice reads - <c>system/setFieldsDict</c>, the initial
/// fields and the files they <c>#include</c> (a swept value reaches an
/// <c>internalField</c> through a reference to an included entry as often
/// as it is written in the field), as the study templated them, tokens in
/// place, from the node's blob cache - given to the Model, the sentences
/// kept as the variant's notes: ranks that do nothing are an
/// <see cref="FoamNoteKind.Info"/> (the values stand, obtained on one rank),
/// a <c>setFields</c> over a swept field a <see cref="FoamNoteKind.Warning"/>
/// (the swept value does not reach the solve, or reaches it outside the
/// regions only); neither names a column. A file that cannot be read says nothing.
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
    /// <returns>One note per sentence of the advice; empty when there is nothing to say.</returns>
    public static async Task<IReadOnlyList<FoamNote>> AdviseAsync(FoamTaskData task, IWitBlobService blobService, CancellationToken cancellationToken = default)
    {
        var advice = FoamCaseAdvice.Of(task.Case)
            .Select(sentence => new FoamNote { Kind = FoamNoteKind.Info, Text = sentence })
            .ToList();
        if (task.Case?.Recipe == null || task.Case.Recipe.Steps.All(step => step.Utility != "setFields"))
            return advice;

        var byPath = new Dictionary<string, FoamFileRefData>(StringComparer.Ordinal);
        foreach (var file in task.Case.BaseFiles)
            byPath.TryAdd(file.RelativePath, file);

        // The dictionary and the initial fields first, then what each field
        // read includes, each file once.
        var wanted = new Queue<string>(byPath.Keys.Where(path => path == SET_FIELDS_DICT || IsInitialField(path)));
        var read = new HashSet<string>(StringComparer.Ordinal);
        var files = new List<(string RelativePath, string? Text)>();
        while (wanted.TryDequeue(out var path))
        {
            if (!read.Add(path) || !byPath.TryGetValue(path, out var file) || !FoamCaseContentRules.IsScanned(path, file.Size))
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            var text = await ReadBlobAsync(blobService, file, cancellationToken);
            files.Add((path, text));

            if (text != null && path != SET_FIELDS_DICT)
            {
                foreach (var included in FoamCaseFunctionObjects.IncludedFiles(text, path))
                    wanted.Enqueue(included);
            }
        }

        advice.AddRange(FoamCaseAdvice.OfSetFields(task.Case.Recipe, files)
            .Select(sentence => new FoamNote { Kind = FoamNoteKind.Warning, Text = sentence }));
        return advice;
    }

    #endregion

    #region Tools

    /// <summary>A file directly under an initial folder: <c>0/U</c>, not <c>0/include/initialConditions</c>.</summary>
    private static bool IsInitialField(string relativePath)
    {
        var folder = INITIAL_FOLDERS.FirstOrDefault(folder => relativePath.StartsWith(folder, StringComparison.Ordinal));
        return folder != null && !relativePath[folder.Length..].Contains('/');
    }

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
