using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// What the controller module of the process that answers carries: its version, the stage set
/// and whether it reproduces its reference product here, the parser and compressor versions,
/// the language-model format and the reference model's hash, the rule of where a machine
/// fetches from, and the caps. A plain script function runs on the host, so through
/// <c>CorpusRuntimeDiagnostics</c> this describes the server's module; the nodes run the same
/// version (the platform admits no other to the work). Mirrors Verify.RuntimeDiagnostics.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class CorpusRuntimeDiagnosticsData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusRuntimeDiagnosticsData other)
            return false;

        return ControllerVersion.Is(other.ControllerVersion)
               && RuntimeTarget.Is(other.RuntimeTarget)
               && Stages.Is(other.Stages)
               && StageSetSha256.Is(other.StageSetSha256)
               && StagesVerified.Is(other.StagesVerified)
               && HtmlParser.Is(other.HtmlParser)
               && Compressor.Is(other.Compressor)
               && LanguageModelFormat.Is(other.LanguageModelFormat)
               && ReferenceLanguageModelSha256.Is(other.ReferenceLanguageModelSha256)
               && PublicOrigins.Is(other.PublicOrigins)
               && MachineOrigins.Is(other.MachineOrigins)
               && MaxShardBytes.Is(other.MaxShardBytes)
               && DownloadsPerNode.Is(other.DownloadsPerNode)
               && MemoryPerShardBytes.Is(other.MemoryPerShardBytes)
               && PdfModuleSha256.Is(other.PdfModuleSha256)
               && PdfStageProblem.Is(other.PdfStageProblem);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusRuntimeDiagnosticsData Clone()
    {
        return new CorpusRuntimeDiagnosticsData
        {
            ControllerVersion = ControllerVersion,
            RuntimeTarget = RuntimeTarget,
            Stages = [.. Stages],
            StageSetSha256 = StageSetSha256,
            StagesVerified = StagesVerified,
            HtmlParser = HtmlParser,
            Compressor = Compressor,
            LanguageModelFormat = LanguageModelFormat,
            ReferenceLanguageModelSha256 = ReferenceLanguageModelSha256,
            PublicOrigins = [.. PublicOrigins],
            MachineOrigins = [.. MachineOrigins],
            MaxShardBytes = MaxShardBytes,
            DownloadsPerNode = DownloadsPerNode,
            MemoryPerShardBytes = MemoryPerShardBytes,
            PdfModuleSha256 = PdfModuleSha256,
            PdfStageProblem = PdfStageProblem
        };
    }

    #endregion

    #region Properties

    /// <summary>The controller version of the module that answered.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public string ControllerVersion { get; set; } = "";

    /// <summary>OS and architecture of the answering process, e.g. <c>linux-x64</c>.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public string RuntimeTarget { get; set; } = "";

    /// <summary>The stages in the order a document meets them.</summary>
    [MemoryPackOrder(2)]
    public List<string> Stages { get; set; } = [];

    /// <summary>The content hash this process computed for the reference shard through the stages: the stage set's fingerprint.</summary>
    [MemoryPackOrder(3)]
    public string StageSetSha256 { get; set; } = "";

    /// <summary>True when that hash is the one the controller pins: the stages compute here what they compute everywhere.</summary>
    [ToString]
    [MemoryPackOrder(4)]
    public bool StagesVerified { get; set; }

    /// <summary>The HTML parser behind the WARC text extraction, name and version.</summary>
    [ToString]
    [MemoryPackOrder(5)]
    public string HtmlParser { get; set; } = "";

    /// <summary>The compressor of the text product, name, version and level.</summary>
    [MemoryPackOrder(6)]
    public string Compressor { get; set; } = "";

    /// <summary>The language-model file format the controller reads (a model is a job's input, pinned by hash).</summary>
    [MemoryPackOrder(7)]
    public string LanguageModelFormat { get; set; } = "";

    /// <summary>SHA-256 of the reference language model, the one the controller's tests and fleet proofs use.</summary>
    [MemoryPackOrder(8)]
    public string ReferenceLanguageModelSha256 { get; set; } = "";

    /// <summary>The public origins every machine fetches from (HTTPS, default port, public addresses only).</summary>
    [MemoryPackOrder(9)]
    public List<string> PublicOrigins { get; set; } = [];

    /// <summary>The further hosts the answering machine's operator lists (taken as written); a job's own list narrows, never widens.</summary>
    [MemoryPackOrder(10)]
    public List<string> MachineOrigins { get; set; } = [];

    /// <summary>The controller's cap on a shard on the wire; a job's option can only lower it.</summary>
    [ToString]
    [MemoryPackOrder(11)]
    public long MaxShardBytes { get; set; }

    /// <summary>Shards a node downloads at once, whatever its degree.</summary>
    [MemoryPackOrder(12)]
    public int DownloadsPerNode { get; set; }

    /// <summary>Memory a node budgets per shard in flight; with the cores it bounds the node's degree.</summary>
    [MemoryPackOrder(13)]
    public long MemoryPerShardBytes { get; set; }

    /// <summary>SHA-256 of the PDF extractor module the controller pins (the PDF stage runs no other bytes).</summary>
    [MemoryPackOrder(14)]
    public string PdfModuleSha256 { get; set; } = "";

    /// <summary>Why the answering process cannot run the PDF stage (no module, another binding, no native library); "" when it can.</summary>
    [MemoryPackOrder(15)]
    public string PdfStageProblem { get; set; } = "";

    #endregion
}
