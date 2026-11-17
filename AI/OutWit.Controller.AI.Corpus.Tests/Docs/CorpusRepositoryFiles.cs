namespace OutWit.Controller.AI.Corpus.Tests.Docs;

/// <summary>
/// The files of the repository the documentation tests read: the controller's documents, its
/// bundled scripts and the demo kit. Text is read with its line ends as line feeds, whatever the
/// checkout made of them, so a test gives the same answer on every platform.
/// </summary>
internal static class CorpusRepositoryFiles
{
    #region Constants

    private const string SOLUTION = "OutWit.slnx";

    #endregion

    #region Functions

    /// <summary>A file of the repository as text, its line ends as line feeds.</summary>
    /// <param name="parts">The path from the repository's root.</param>
    /// <returns>The text.</returns>
    public static string ReadText(params string[] parts)
    {
        return File.ReadAllText(PathOf(parts)).Replace("\r\n", "\n");
    }

    /// <summary>The full path of a file of the repository.</summary>
    /// <param name="parts">The path from the repository's root.</param>
    /// <returns>The path.</returns>
    public static string PathOf(params string[] parts)
    {
        return Path.Combine([Root, .. parts]);
    }

    #endregion

    #region Properties

    /// <summary>The repository's root: the folder of the solution file, found upwards from the test output.</summary>
    public static string Root
    {
        get
        {
            var directory = TestContext.CurrentContext.TestDirectory;
            while (directory != null && !File.Exists(Path.Combine(directory, SOLUTION)))
                directory = Path.GetDirectoryName(directory);

            return directory ?? throw new InvalidOperationException($"The repository's root ({SOLUTION}) was not found above the test output.");
        }
    }

    #endregion
}
