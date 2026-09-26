using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// The controller's own <c>restore0Dir -processor</c>, done as OpenFOAM's
/// <c>bin/tools/RunFunctions</c> does it: every processor directory's
/// <c>0/</c> is replaced by the case's initial fields. That is what a case
/// meshed on its decomposed form needs: <c>decomposePar</c> split the fields
/// over the background mesh, the meshing changed the mesh under them. The
/// fields come from <c>0.orig/</c> when the case carries one, as in
/// OpenFOAM; otherwise from <c>0/</c> as it was before the first step, which
/// the runner keeps in <c>0.orig/</c> for the purpose - not a time directory,
/// so no artifact takes it. A serial run has no processor directories, and
/// its <c>0/</c> already holds the initial fields: nothing to do.
/// </summary>
public static class FoamInitialFields
{
    #region Constants

    public const string INITIAL = "0";

    public const string INITIAL_ORIG = "0.orig";

    /// <summary>A processor directory of the uncollated layout, the only one the controller writes.</summary>
    private static readonly Regex PROCESSOR_NAME = new(@"^processor\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    #endregion

    #region Functions

    /// <summary>
    /// Keeps the initial fields before the first step can change them: a copy
    /// of <c>0/</c> in <c>0.orig/</c>, unless the case carries its own.
    /// </summary>
    /// <param name="caseDirectory">The materialised case.</param>
    public static void Keep(string caseDirectory)
    {
        var initial = Path.Combine(caseDirectory, INITIAL);
        var orig = Path.Combine(caseDirectory, INITIAL_ORIG);
        if (Directory.Exists(orig) || !Directory.Exists(initial))
            return;

        CopyDirectory(initial, orig);
    }

    /// <summary>
    /// Replaces every processor directory's <c>0/</c> by the initial fields.
    /// </summary>
    /// <param name="caseDirectory">The case.</param>
    /// <param name="logPath">The step's log, written whatever happens.</param>
    /// <returns>The step's outcome: 0 when done or when there was nothing to do.</returns>
    public static FoamRunOutcome RestoreIntoProcessors(string caseDirectory, string logPath)
    {
        var stopwatch = Stopwatch.StartNew();
        var log = new StringBuilder();
        var exitCode = 0;

        try
        {
            var processors = Directory.EnumerateDirectories(caseDirectory)
                .Where(directory => PROCESSOR_NAME.IsMatch(Path.GetFileName(directory)))
                .OrderBy(directory => int.Parse(Path.GetFileName(directory)["processor".Length..]))
                .ToList();
            var source = SourceOf(caseDirectory);

            if (processors.Count == 0)
                log.AppendLine("No processor directories: the run is serial, and its 0/ already holds the initial fields.");
            else if (source == null)
                log.AppendLine("No 0.orig/ to restore...");
            else
            {
                log.AppendLine($"Restore 0/ from {Path.GetFileName(source)}/  [processor dirs]");
                foreach (var processor in processors)
                {
                    var target = Path.Combine(processor, INITIAL);
                    if (Directory.Exists(target))
                        Directory.Delete(target, recursive: true);

                    CopyDirectory(source, target);
                    log.AppendLine($"    {Path.GetFileName(processor)}/{INITIAL}");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.AppendLine($"--> the initial fields could not be restored: {exception.Message}");
            exitCode = 1;
        }

        var text = log.ToString();
        File.WriteAllText(logPath, text);
        return new FoamRunOutcome(exitCode, stopwatch.Elapsed.TotalSeconds, text);
    }

    /// <summary>Where the initial fields are: <c>0.orig/</c>, as OpenFOAM's restore reads them; null when the case has none.</summary>
    private static string? SourceOf(string caseDirectory)
    {
        var orig = Path.Combine(caseDirectory, INITIAL_ORIG);
        return Directory.Exists(orig) ? orig : null;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? target);
            File.Copy(file, destination, overwrite: true);
        }
    }

    #endregion
}
