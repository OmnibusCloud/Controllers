namespace OutWit.Controller.AI.Smt.Live.Documents;

/// <summary>
/// The documents of the controller's scripts and where their schemas are: in <c>docs/</c> beside
/// the tool, copied there from the controller's own <c>docs/</c> when the tool is built.
/// </summary>
internal static class SmtLiveDocuments
{
    #region Constants

    /// <summary>The folder beside the tool that holds the schemas.</summary>
    public const string DOCS_FOLDER = "docs";

    /// <summary>The results of <c>SmtRun</c>: one row a row of the set.</summary>
    public static readonly SmtLiveDocument RESULTS = new("results", "results.jsonl", "smt-results.schema.json", true);

    /// <summary>The report of <c>SmtRun</c>.</summary>
    public static readonly SmtLiveDocument REPORT = new("report", "report.json", "smt-report.schema.json", false);

    /// <summary>The integrity document of <c>SmtRun</c>.</summary>
    public static readonly SmtLiveDocument INTEGRITY = new("integrity", "integrity.json", "smt-integrity.schema.json", false);

    /// <summary>The provenance of <c>SmtRun</c>.</summary>
    public static readonly SmtLiveDocument PROVENANCE = new("provenance", "provenance.json", "smt-provenance.schema.json", false);

    /// <summary>The capabilities document of <c>SmtRuntimeDiagnostics</c>.</summary>
    public static readonly SmtLiveDocument CAPABILITIES = new("capabilities", "capabilities.json", "smt-capabilities.schema.json", false);

    /// <summary>The four documents of a run, in the order they are downloaded.</summary>
    public static readonly IReadOnlyList<SmtLiveDocument> RUN = [RESULTS, REPORT, INTEGRITY, PROVENANCE];

    #endregion

    #region Functions

    /// <summary>
    /// Loads the schema of a document from beside the tool.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>Its schema.</returns>
    /// <exception cref="FileNotFoundException">The schema is not beside the tool.</exception>
    /// <exception cref="InvalidDataException">The schema uses a keyword the tool does not check.</exception>
    public static SmtLiveSchema SchemaOf(SmtLiveDocument document)
    {
        var path = Path.Combine(AppContext.BaseDirectory, DOCS_FOLDER, document.Schema);
        if (!File.Exists(path))
            throw new FileNotFoundException($"the schema {document.Schema} is not beside the tool (expected in {DOCS_FOLDER}/)", path);

        return SmtLiveSchema.Load(path);
    }

    #endregion
}
