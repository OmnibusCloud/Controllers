namespace OutWit.Controller.AI.Corpus.PdfSpike.Runner;

/// <summary>One document through one engine: the verdict, the text's identity and the cost.</summary>
public sealed class PdfRunRecord
{
    #region Properties

    /// <summary>The engine's name.</summary>
    public string Engine { get; set; } = "";

    /// <summary>The file name.</summary>
    public string File { get; set; } = "";

    /// <summary>The input size in bytes.</summary>
    public long Bytes { get; set; }

    /// <summary>ok, error, oom, timeout, memory (killed over the cap) or crash (the process died).</summary>
    public string Verdict { get; set; } = "";

    /// <summary>The exception type when the document failed.</summary>
    public string? Exception { get; set; }

    /// <summary>The first line of the exception message, clipped.</summary>
    public string? Message { get; set; }

    /// <summary>The page count the engine found.</summary>
    public int Pages { get; set; }

    /// <summary>The pages read.</summary>
    public int PagesRead { get; set; }

    /// <summary>The pages whose extraction threw.</summary>
    public int PagesFailed { get; set; }

    /// <summary>The exception type of the first failing page.</summary>
    public string? FirstPageError { get; set; }

    /// <summary>The characters of text.</summary>
    public int Chars { get; set; }

    /// <summary>U+FFFD characters in the text.</summary>
    public int Replacements { get; set; }

    /// <summary>SHA-256 of the text's UTF-8 bytes as the engine produced them.</summary>
    public string? Sha256 { get; set; }

    /// <summary>SHA-256 of the text with every CR LF and lone CR turned into LF.</summary>
    public string? Sha256Lf { get; set; }

    /// <summary>The extraction time measured inside the process, ms.</summary>
    public double Ms { get; set; }

    /// <summary>The wall time seen by the parent, process start included, ms.</summary>
    public double WallMs { get; set; }

    /// <summary>The highest working set the parent saw, MB.</summary>
    public double PeakMb { get; set; }

    /// <summary>The peak working set the process reported for itself (0 where the platform does not say), MB.</summary>
    public double SelfPeakMb { get; set; }

    /// <summary>The bytes the process allocated on the managed heap, MB.</summary>
    public double AllocatedMb { get; set; }

    /// <summary>The child's exit code.</summary>
    public int ExitCode { get; set; }

    #endregion
}
