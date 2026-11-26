using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Local;

namespace OutWit.Controller.AI.Smt.Live.Commands;

/// <summary>
/// The folder a run's documents go to (<c>run</c>, <c>fetch</c>, <c>local</c>). It is made, and
/// held to be writable and free of another run's documents, before anything is sent or run: a
/// folder that cannot be written is found before the fleet has done the work, and a folder that
/// holds an earlier run's documents is refused rather than mixed (<c>compare</c>,
/// <c>comparative</c> and the kits' checks would read the two runs as one).
/// </summary>
internal static class SmtLiveFolder
{
    #region Constants

    /// <summary>The files of a run's folder: the four documents and what the tool measured.</summary>
    public static readonly IReadOnlyList<string> FILES =
        [.. SmtLiveDocuments.RUN.Select(document => document.File), SmtLiveFleetFacts.FILE, SmtLiveLocalFacts.FILE];

    private const string WRITE_CHECK = ".smt-live-write-check";

    #endregion

    #region Functions

    /// <summary>
    /// Makes a run's folder and holds it to be writable and free.
    /// </summary>
    /// <param name="folder">The folder; made when it is not there.</param>
    /// <param name="problem">Why it cannot be used; null when it can.</param>
    /// <returns>True when it can be used.</returns>
    public static bool Prepare(string folder, out string? problem)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var held = FILES.Where(file => File.Exists(Path.Combine(folder, file))).ToList();
            if (held.Count > 0)
            {
                problem = $"the folder {folder} already holds {string.Join(", ", held)}, the documents of another run: name another --out, or empty it";
                return false;
            }

            var check = Path.Combine(folder, WRITE_CHECK);
            File.WriteAllBytes(check, []);
            File.Delete(check);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            problem = $"the folder {folder} cannot be written: {exception.Message}";
            return false;
        }

        problem = null;
        return true;
    }

    /// <summary>
    /// Whether a folder holds any file of a run.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <returns>True when one of <see cref="FILES"/> is in it.</returns>
    public static bool HoldsDocuments(string folder)
    {
        return FILES.Any(file => File.Exists(Path.Combine(folder, file)));
    }

    /// <summary>
    /// Removes a run's folder that got nothing (a job that failed, a stop before any document):
    /// no empty folder is left behind.
    /// </summary>
    /// <param name="folder">The folder.</param>
    public static void RemoveIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // an empty folder of the tool's own; nothing is lost when it stays
        }
    }

    #endregion
}
