using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Manifests;

/// <summary>
/// A parsed manifest: the pipeline, the origins the lab allows, the sources, and every problem
/// the parser found (a manifest with errors never becomes tasks).
/// </summary>
/// <param name="Pipeline">The pipeline contract (defaults where the manifest said nothing).</param>
/// <param name="AllowedOrigins">Host names the nodes may fetch from.</param>
/// <param name="Sources">The shards, in manifest order.</param>
/// <param name="Errors">Problems, each naming where it was found.</param>
public sealed record CorpusManifest(CorpusPipelineData Pipeline, List<string> AllowedOrigins, List<CorpusSourceData> Sources, List<string> Errors);
