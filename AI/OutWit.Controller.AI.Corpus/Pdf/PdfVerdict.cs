namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>
/// What one PDF came to in the sandbox. Every verdict is a function of the PDF's bytes and the
/// stage's contract (<see cref="PdfStageLimits"/>), the same on every node; a node that cannot run
/// the sandbox gives no verdict at all (<see cref="PdfSandboxException"/>).
/// </summary>
public enum PdfVerdict
{
    /// <summary>The module wrote the text (possibly empty pages).</summary>
    Text,

    /// <summary>The PDF needs a password, or its security handler cannot run.</summary>
    Encrypted,

    /// <summary>The bytes cannot be opened as a PDF, or its pages cannot be counted.</summary>
    Unreadable,

    /// <summary>The PDF has no page.</summary>
    NoPages,

    /// <summary>The fuel ran out.</summary>
    Fuel,

    /// <summary>The module ended for want of memory: a grow past the memory cap was refused.</summary>
    Memory,

    /// <summary>The module crashed (a trap other than a budget stop: an abort, a stack that overflowed).</summary>
    Trap,

    /// <summary>The module failed otherwise, or broke its output contract.</summary>
    Error,

    /// <summary>The PDF, or the text the module wrote for it, is larger than the stage takes.</summary>
    Oversized
}
