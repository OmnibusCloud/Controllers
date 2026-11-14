using System.Reflection;
using System.Runtime.InteropServices;
using OutWit.Controller.AI.Corpus.Compression;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Diagnostics;

/// <summary>
/// Builds what the controller module of this process carries: its version, the stage set and
/// its fingerprint computed here, the parser and compressor versions, the language-model format
/// and the reference model's hash, the rule of where this machine fetches from, the caps.
/// A plain script function evaluates on the host, so through <c>CorpusRuntimeDiagnostics</c>
/// this describes the server's module. Mirrors Verify's diagnostics.
/// </summary>
public static class CorpusRuntimeDiagnosticsBuilder
{
    #region Functions

    /// <summary>Builds the diagnostics of this process, running the reference shard once.</summary>
    /// <param name="policy">What this machine fetches from (null = this machine's policy).</param>
    /// <param name="cancellationToken">Cancels the reference run.</param>
    /// <returns>The diagnostics record.</returns>
    public static CorpusRuntimeDiagnosticsData Build(ShardOriginPolicy? policy = null, CancellationToken cancellationToken = default)
    {
        policy ??= ShardOriginPolicy.Machine;
        var (sha256, verified) = CorpusStageSet.Fingerprint(cancellationToken);

        return new CorpusRuntimeDiagnosticsData
        {
            ControllerVersion = ControllerBuildInfo.VERSION,
            RuntimeTarget = RuntimeTarget(),
            Stages = [.. CorpusStageSet.STAGES],
            StageSetSha256 = sha256,
            StagesVerified = verified,
            HtmlParser = HtmlParser(),
            Compressor = Compressor(),
            LanguageModelFormat = CorpusReferenceLanguageModel.FORMAT,
            ReferenceLanguageModelSha256 = CorpusReferenceLanguageModel.SHA256,
            PublicOrigins = [.. ShardOriginPolicy.PUBLIC_ORIGINS],
            MachineOrigins = policy.ExtraOrigins.Order(StringComparer.Ordinal).ToList(),
            MaxShardBytes = CorpusTaskPlanner.MAX_SHARD_BYTES,
            DownloadsPerNode = ShardFetchPolicy.Default.MaxConcurrent,
            MemoryPerShardBytes = ShardDegree.MEMORY_PER_SHARD
        };
    }

    /// <summary>OS and architecture as the diagnostics name them, e.g. <c>linux-x64</c>.</summary>
    /// <returns>The runtime target.</returns>
    public static string RuntimeTarget()
    {
        var os = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "macos"
            : "unknown";
        return $"{os}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
    }

    /// <summary>The HTML parser behind the WARC text extraction, e.g. <c>AngleSharp 1.10.0</c>.</summary>
    /// <returns>Name and version.</returns>
    public static string HtmlParser()
    {
        return $"AngleSharp {LibraryVersion(typeof(AngleSharp.Html.Parser.HtmlParser).Assembly)}";
    }

    /// <summary>The compressor of the text product, e.g. <c>ZstdSharp 0.8.8, level 9</c>.</summary>
    /// <returns>Name, version and level.</returns>
    public static string Compressor()
    {
        return $"ZstdSharp {LibraryVersion(typeof(ZstdSharp.Compressor).Assembly)}, level {CorpusCompressor.LEVEL}";
    }

    private static string LibraryVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    #endregion
}
