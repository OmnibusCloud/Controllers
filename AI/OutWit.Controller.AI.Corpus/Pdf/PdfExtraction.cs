namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>What the sandbox made of one PDF.</summary>
/// <param name="Verdict">How the run ended.</param>
/// <param name="Text">The module's text when <paramref name="Verdict"/> is <see cref="PdfVerdict.Text"/> (pages separated by U+000C), else "".</param>
/// <param name="Pages">The pages the module counted (0 when it did not say).</param>
/// <param name="Encrypted">Whether the PDF is encrypted (read with the empty password when the verdict is text).</param>
/// <param name="Fuel">The fuel the run burned from <c>_start</c> on (the whole budget when it ran out); 0 when nothing ran.</param>
public sealed record PdfExtraction(PdfVerdict Verdict, string Text, int Pages, bool Encrypted, long Fuel)
{
    #region Functions

    /// <summary>An extraction that ended without text.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <param name="fuel">The fuel burned.</param>
    /// <param name="pages">The pages the module counted.</param>
    /// <param name="encrypted">Whether the module found the PDF encrypted.</param>
    /// <returns>The extraction.</returns>
    public static PdfExtraction Refused(PdfVerdict verdict, long fuel, int pages = 0, bool encrypted = false)
    {
        return new PdfExtraction(verdict, "", pages, encrypted, fuel);
    }

    #endregion
}
