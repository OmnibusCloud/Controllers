using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// Takes a test's work folder away. The pipeline hands the folders of finished PDF runs and of
/// finished tasks to a thread nobody waits for (<see cref="ShardCleanup"/>), so a fixture that
/// deletes its folder the moment its last test ends would meet that thread half-way: it waits for
/// what was handed over, and a file something still holds is left to the system's temp cleaning
/// rather than failing the fixture.
/// </summary>
internal static class CorpusTestFolders
{
    #region Constants

    private static readonly TimeSpan PATIENCE = TimeSpan.FromMinutes(2);

    #endregion

    #region Functions

    /// <summary>Deletes a folder of a test run with everything in it, after the pipeline's own deletions are done.</summary>
    /// <param name="directory">The folder; null or missing is nothing to do.</param>
    public static void Delete(string? directory)
    {
        ShardCleanup.Drain(PATIENCE);

        if (directory == null || !Directory.Exists(directory))
            return;

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder of a test run; no later test uses it.
        }
    }

    #endregion
}
