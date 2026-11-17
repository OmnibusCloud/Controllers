using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Adapters;

/// <summary>A job as the host planned it, with what the plan was made from.</summary>
/// <param name="Plan">The plan.</param>
/// <param name="Manifest">The parsed manifest it came from.</param>
/// <param name="Options">The job's options (null = defaults).</param>
/// <param name="InputBytes">The bytes of the inputs (manifest and language model) in the store.</param>
/// <param name="Model">What the host read in the language model blob; null when the job has none.</param>
/// <param name="ModelBytes">The size of the language model the nodes will download; 0 when the pipeline uses none.</param>
internal sealed record CorpusPlanned(CorpusPlan Plan, CorpusManifest Manifest, CorpusOptionsData? Options, long InputBytes, CorpusLanguageModelFacts? Model, long ModelBytes);
