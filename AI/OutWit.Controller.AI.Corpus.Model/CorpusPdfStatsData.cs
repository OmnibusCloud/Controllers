using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// What the PDF stage did with one WARC shard's PDFs: how many came in (and how many of them
/// Common Crawl had cut short), how many gave text, their pages, the page-range documents they
/// became, the cuts and the running lines the shaping made, and the fuel the sandbox burned. The
/// PDFs that gave nothing are in the shard's drops under the <c>pdf_*</c> rules. Every count is a
/// function of the shard and the pipeline.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class CorpusPdfStatsData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusPdfStatsData other)
            return false;

        return In.Is(other.In)
               && Truncated.Is(other.Truncated)
               && Extracted.Is(other.Extracted)
               && Pages.Is(other.Pages)
               && Documents.Is(other.Documents)
               && PagesCut.Is(other.PagesCut)
               && RunningLines.Is(other.RunningLines)
               && Fuel.Is(other.Fuel);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusPdfStatsData Clone()
    {
        return new CorpusPdfStatsData
        {
            In = In,
            Truncated = Truncated,
            Extracted = Extracted,
            Pages = Pages,
            Documents = Documents,
            PagesCut = PagesCut,
            RunningLines = RunningLines,
            Fuel = Fuel
        };
    }

    #endregion

    #region Properties

    /// <summary>PDF responses the stage took (Content-Type or identified payload type application/pdf, the body starting with <c>%PDF-</c>).</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public int In { get; set; }

    /// <summary>Of those, the ones the crawler cut short (<c>WARC-Truncated</c>); they are read all the same.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public int Truncated { get; set; }

    /// <summary>PDFs that gave at least one document.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public int Extracted { get; set; }

    /// <summary>The pages of the PDFs that gave a document.</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public int Pages { get; set; }

    /// <summary>The page-range documents made (each also counts as a document extracted).</summary>
    [ToString]
    [MemoryPackOrder(4)]
    public int Documents { get; set; }

    /// <summary>Pages longer than a document may be, cut after the last line that fits.</summary>
    [MemoryPackOrder(5)]
    public int PagesCut { get; set; }

    /// <summary>Lines removed as running headers and footers.</summary>
    [MemoryPackOrder(6)]
    public int RunningLines { get; set; }

    /// <summary>The fuel the sandbox burned over every PDF of the shard, counted from each run's <c>_start</c> (deterministic: the same on every node).</summary>
    [ToString]
    [MemoryPackOrder(7)]
    public long Fuel { get; set; }

    #endregion
}
