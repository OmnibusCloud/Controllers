namespace OutWit.Controller.AI.Smt.Live;

/// <summary>What the tool is asked to do: the first word of its command line.</summary>
internal enum SmtLiveVerb
{
    /// <summary>Runs <c>SmtRuntimeDiagnostics</c> and prints the diagnostics and the capabilities document.</summary>
    Diagnostics,

    /// <summary>Uploads a problem set, runs <c>SmtPreflight</c> and prints what it says.</summary>
    Preflight,

    /// <summary>Uploads a problem set, runs <c>SmtRun</c>, downloads and checks the four documents.</summary>
    Run,

    /// <summary>Compares the documents of two runs of one problem set.</summary>
    Compare,

    /// <summary>Packs a folder of SMT-LIB files into a problem set.</summary>
    Pack
}
