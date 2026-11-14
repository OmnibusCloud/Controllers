using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// The deterministic processing contract a shard goes through: which stages run, with which
/// parameters, against which pinned model. Its version is part of the verification identity —
/// the same shard through the same pipeline gives the same bytes on any node.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class CorpusPipelineData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusPipelineData other)
            return false;

        return Version.Is(other.Version)
               && LanguageModelSha256.Is(other.LanguageModelSha256)
               && TargetLanguages.Is(other.TargetLanguages)
               && IdentifyLanguage.Is(other.IdentifyLanguage)
               && FilterQuality.Is(other.FilterQuality)
               && Sign.Is(other.Sign)
               && Quality.Check(other.Quality)
               && LanguageModelBlobId.Is(other.LanguageModelBlobId)
               && CleanLines.Is(other.CleanLines)
               && ExtractPdf.Is(other.ExtractPdf);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusPipelineData Clone()
    {
        return new CorpusPipelineData
        {
            Version = Version,
            LanguageModelSha256 = LanguageModelSha256,
            TargetLanguages = [.. TargetLanguages],
            IdentifyLanguage = IdentifyLanguage,
            FilterQuality = FilterQuality,
            Sign = Sign,
            Quality = Quality?.Clone(),
            LanguageModelBlobId = LanguageModelBlobId,
            CleanLines = CleanLines,
            ExtractPdf = ExtractPdf
        };
    }

    #endregion

    #region Properties

    /// <summary>The pipeline version a lab names (free text, e.g. "cc-text-v1"); travels into every product.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public string Version { get; set; } = "";

    /// <summary>Lower-case hex SHA-256 of the language model the node must use ("" = the language stage is off).</summary>
    [MemoryPackOrder(1)]
    public string LanguageModelSha256 { get; set; } = "";

    /// <summary>Languages to keep (ISO 639-3); empty = keep every language.</summary>
    [MemoryPackOrder(2)]
    public List<string> TargetLanguages { get; set; } = [];

    /// <summary>Whether the language stage runs.</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public bool IdentifyLanguage { get; set; } = true;

    /// <summary>Whether the quality filter runs.</summary>
    [ToString]
    [MemoryPackOrder(4)]
    public bool FilterQuality { get; set; } = true;

    /// <summary>Whether MinHash signatures are produced.</summary>
    [ToString]
    [MemoryPackOrder(5)]
    public bool Sign { get; set; } = true;

    /// <summary>The quality thresholds; null = the stage defaults.</summary>
    [MemoryPackOrder(6)]
    public CorpusQualityData? Quality { get; set; }

    /// <summary>The blob that carries the language model (Guid.Empty = none); the node checks it against <see cref="LanguageModelSha256"/>.</summary>
    [MemoryPackOrder(7)]
    public Guid LanguageModelBlobId { get; set; }

    /// <summary>Whether the boilerplate-line stage runs: lines that do not read like prose (navigation, prices, footers) leave each document before it is judged.</summary>
    [ToString]
    [MemoryPackOrder(8)]
    public bool CleanLines { get; set; } = true;

    /// <summary>
    /// Whether the PDF stage runs on WARC shards: a response that is a PDF becomes text in the
    /// controller's wasm sandbox and then page-range documents that go through the stages after
    /// it like a page of HTML. Off, a PDF drops as <c>not_html</c> as before the stage existed.
    /// WET shards carry no PDFs.
    /// </summary>
    [ToString]
    [MemoryPackOrder(9)]
    public bool ExtractPdf { get; set; } = true;

    #endregion
}
