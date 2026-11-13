using System.Diagnostics;

namespace OutWit.Controller.AI.Corpus.PdfSpike.Guards;

/// <summary>
/// What one document may cost while its streams are decoded: the bytes one stream may inflate
/// to, the bytes all of its streams together may inflate to (decoding the same stream again
/// counts again, so a form drawn a million times spends the budget a million times), and a
/// deadline checked at every decode and between pages.
/// </summary>
public sealed class PdfDecodeBudget
{
    #region Fields

    private readonly Stopwatch m_clock = Stopwatch.StartNew();
    private long m_spent;

    #endregion

    #region Constructors

    /// <summary>Creates a budget; the clock starts now.</summary>
    /// <param name="maxStreamBytes">The most one decoded stream may hold.</param>
    /// <param name="maxDocumentBytes">The most all decoded streams of the document may hold together.</param>
    /// <param name="deadline">The time the document may take.</param>
    public PdfDecodeBudget(long maxStreamBytes, long maxDocumentBytes, TimeSpan deadline)
    {
        MaxStreamBytes = maxStreamBytes;
        MaxDocumentBytes = maxDocumentBytes;
        Deadline = deadline;
    }

    #endregion

    #region Functions

    /// <summary>Spends decoded bytes; throws when a cap is passed.</summary>
    /// <param name="bytes">The bytes one decode produced.</param>
    /// <exception cref="PdfBudgetExceededException">A cap or the deadline is passed.</exception>
    public void Spend(long bytes)
    {
        if (bytes > MaxStreamBytes)
            throw new PdfBudgetExceededException($"a stream inflates past {MaxStreamBytes} bytes");

        m_spent += bytes;
        if (m_spent > MaxDocumentBytes)
            throw new PdfBudgetExceededException($"the document's streams inflate past {MaxDocumentBytes} bytes");

        CheckDeadline();
    }

    /// <summary>Throws when the deadline has passed.</summary>
    /// <exception cref="PdfBudgetExceededException">The deadline is passed.</exception>
    public void CheckDeadline()
    {
        if (m_clock.Elapsed > Deadline)
            throw new PdfBudgetExceededException($"the document took longer than {Deadline.TotalSeconds:0} s");
    }

    #endregion

    #region Properties

    /// <summary>The most one decoded stream may hold.</summary>
    public long MaxStreamBytes { get; }

    /// <summary>The most all decoded streams of the document may hold together.</summary>
    public long MaxDocumentBytes { get; }

    /// <summary>The time the document may take.</summary>
    public TimeSpan Deadline { get; }

    /// <summary>The decoded bytes spent so far.</summary>
    public long Spent => m_spent;

    #endregion
}
