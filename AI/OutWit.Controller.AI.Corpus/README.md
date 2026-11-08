# OutWit.Controller.AI.Corpus

Public-corpus processing at CPU scale on anonymous volunteer nodes: raw web
data (Common Crawl first) becomes training-grade text — extraction, cleaning,
language identification, quality filtering and deduplication signatures —
with every stage deterministic by requirement, so the same shard through the
same pipeline version gives byte-identical output on any node and a sampled
re-execution can check it.

The data path is the novelty: the task descriptor carries a URL and the node
fetches its shard straight from the public origin (Pattern U); the platform
transports descriptors and results only. That capability comes with its
obligations — a shard cap and a traffic estimate the contributor sees before
opting in, an origin allowlist checked on the host and again on the node,
retries with jitter towards the origin.

**Status: 0.1.0-dev — CO-2: the controller runs end to end.** The stage
libraries (CO-1) are wired into four activities and two bundled scripts; the
engine rig in the tests fetches the golden shards from a loopback origin
through mock nodes and reproduces a direct run's content hashes. Not yet
released; the fleet proof on the test server (CO-3) comes next.

## Activities

| Activity | Runs on | Signature | What it does |
|---|---|---|---|
| `Corpus.Split` | host | `(Blob manifest, Blob languageModel, CorpusOptions) → CorpusTaskCollection` | parses the manifest, checks every source against the allowlist and the byte cap, hashes the language model blob and pins it into the pipeline, emits one task per shard. Any problem fails the job here — a bad manifest never reaches a node |
| `Corpus.Preflight` | host | `(Blob manifest, Blob languageModel, CorpusOptions) → CorpusPreflight` | the same checks, reported instead of thrown: shard count, refused hosts, oversized sources, the traffic estimate, every message by manifest line |
| `Corpus.ProcessShard` | node | `(CorpusTask) → CorpusResult` | the allowlist re-check, the pinned model, the capped fetch, the pipeline, the uploads. Every way a shard can fail is a verdict in the result (`OriginRefused`, `FetchFailed`, `Oversized`, `Malformed`, `PipelineUnavailable`, `Failed`), never an exception; several shards may run on one client at once |
| `Corpus.Collect` | host | `(CorpusResultCollection, CorpusOptions) → Blob report` | the job report: every shard's verdict, blobs and content hash in task order, the aggregate counters over the fleet |
| `Corpus.Sample` | host | `(CorpusTaskCollection, CorpusOptions) → CorpusTaskCollection` | the sampled share of the tasks (`SamplingRate`) as replica tasks for a second fan-out, chosen by index alone and handed over in reverse so the allocator lands most of them away from their primaries |
| `Corpus.Integrity` | host | `(CorpusResultCollection results, CorpusResultCollection replicas) → Blob integrity` | primary and replica of every sampled shard compared on verdict, content hash, document counts and histograms; a pair that landed on one node is counted, not compared |

## Scripts

```
Job:CorpusProcess(Blob:manifest, Blob:languageModel, CorpusOptions:opts)
{
    CorpusTaskCollection:tasks = Corpus.Split(manifest, languageModel, opts);

    CorpusResultCollection:results = Grid.ForEach(task in tasks)
        => Corpus.ProcessShard(task);

    CorpusTaskCollection:replicaTasks = Corpus.Sample(tasks, opts);

    CorpusResultCollection:replicas = Grid.ForEach(task in replicaTasks)
        => Corpus.ProcessShard(task);

    Blob:report = Corpus.Collect(results, opts);
    Blob:integrity = Corpus.Integrity(results, replicas);
}
```

`CorpusPreflight.wit` runs `Corpus.Preflight` alone. `languageModel` is the
blob of a model trained by `corpus-assets` (see below); pass an empty blob when
the pipeline's `identify_language` is `false`. `CorpusOptions`: `MaxShardBytes`
(0 = the controller's cap of 1 GiB; a smaller value lowers it), `SamplingRate`
(the share of shards re-executed for integrity, 0 to 1; 0 by default) and
`FetchConcurrency` (reserved).

## The manifest

One JSON document (`docs/corpus-manifest.schema.json`):

```json
{
  "pipeline": {
    "version": "cc-text-v1",
    "language_model_sha256": "9670e262…",
    "target_languages": ["eng", "deu"],
    "identify_language": true,
    "filter_quality": true,
    "sign": true,
    "quality": { "min_words": 50, "max_duplicate_line_ratio": 0.3 }
  },
  "allowed_origins": ["data.commoncrawl.org"],
  "sources": [
    "https://data.commoncrawl.org/crawl-data/CC-MAIN-2026-43/segments/…/wet/CC-MAIN-…-00000.warc.wet.gz",
    { "url": "https://data.commoncrawl.org/…/warc/CC-MAIN-…-00000.warc.gz", "format": "warc", "size": 1073741824, "sha256": "…" }
  ]
}
```

- `pipeline.version` is the lab's name for the contract and travels into every
  product; change a threshold, change the version. Thresholds left out (or 0)
  are the stage defaults; `language_model_sha256` is optional and, when given,
  must match the model blob or the job does not start.
- `allowed_origins` are host names, matched exactly and case-insensitively
  (ports and paths do not matter); a source whose host is not listed is
  refused on the host and, should it ever get there, on the node.
- A bare URL's format follows its extension (`.warc.wet.gz` → WET,
  `.warc.gz` → WARC); an object may state `format`, a `size` estimate for the
  traffic line and the cap check, and the origin's published `sha256`, which
  the node verifies after the fetch.
- Unknown keys are errors, so a typo cannot silently turn a stage off.

## The products

- **Text blob** (`text.jsonl.zst`): zstd-compressed JSON lines, one per
  document that passed every stage, `{"url","lang","text"}` in that order;
  `lang` is the ISO 639-3 code the pipeline identified ("" when the stage is
  off). The result's `ContentSha256` is the SHA-256 of the *uncompressed*
  lines — the identity a re-execution on another node must reproduce.
- **Signatures blob** (`signatures.bin`): the magic `WITSIG1\n`, the hashes
  per signature (int32 LE, 128), then one MinHash signature per document in
  product order (128 × uint64 LE) — a fixed layout, document *i* at
  `12 + i × 1024`. Absent when `sign` is `false`.
- **Report** (`corpus-report.json`, `docs/corpus-report.schema.json`): per
  shard the verdict, blob ids, content hash, error and statistics (records in,
  documents extracted and out, drops by rule, languages, bytes, times); the
  summary sums them over the fleet.

## Integrity

`SamplingRate` re-executes a share of the shards on the fleet a second time and
`Corpus.Integrity` writes `corpus-integrity.json` (`docs/corpus-integrity.schema.json`):
for every sampled shard the primary and the replica with the nodes that ran
them, compared on everything an honest node cannot vary — the verdict, the
content hash of the product, the record and document counts, the drop and
language histograms. Times and bytes are not compared. A pair whose replica
landed on the primary's node proves nothing about that node and is counted as
`same_node`, not compared; the mismatch rate is over the compared pairs. The
sample is a function of the shard index, so a lab can reproduce the choice.

## Node behaviour

- The node fetches only what the task declares: the URL's host must be on the
  task's allowlist and the scheme HTTP(S); nothing else is ever requested.
- The fetch is streamed to the task's own temp scope and capped: a
  `Content-Length` past the cap ends the request before a byte is read, a
  streamed body past it is cut. Three attempts with doubling back-off and
  jitter on connection failures, 429 and 5xx; a stalled read (60 s without a
  byte) counts as a failure; a 4xx other than 429 is final. A retry after a
  broken body asks for the rest with a Range request and appends when the
  origin honours it. The user agent names the controller and its version.
- The language model arrives as a blob (the engine caches it per node) and is
  trusted only when its bytes hash to the pipeline's pin; a model that does
  not match is `PipelineUnavailable`, not a different result.
- Processing is one streaming pass: one record in memory at a time, the
  product hashed and compressed as it is written, on a thread below normal
  priority (a worker is often a desktop). The task's temp scope is deleted
  when the task ends, whatever the verdict.

## What is here

| Piece | Where | What it does |
|---|---|---|
| `WarcReader` / `WarcRecord` | `Warc/` | streams the records of a gzip (one member per record, as Common Crawl writes them) or plain WARC/WET file, one record in memory at a time; oversized records skipped and counted; a truncated tail ends the file with the complete records; malformed input is an error at an offset |
| `HttpResponsePayload` / `HtmlTextExtractor` | `Text/` | a WARC response's HTTP headers and body, decoded by header charset → meta charset → UTF-8; HTML to text on AngleSharp with a scoped rule set (scripts, styles, navigation, forms and hidden content dropped; blocks become lines; title first); a linear pre-scan refuses pages nested deeper than 512 or past 60k tags. Agrees with Common Crawl's own WET text at a median Jaccard of 0.85 on the golden shard |
| `LanguageIdentifier` / `LanguageModel` / `LanguageModelTrainer` | `Language/` | character n-gram profiles (Cavnar–Trenkle, integer rank distance, ordinal ties); the model is trained by `corpus-assets` from WET files' own language labels and pinned by hash; the committed 16-language model agrees with the crawl's labels at 98.8 % on the shard |
| `QualityFilter` / `QualityRules` | `Quality/` | the Gopher-style shape and repetition rules, every threshold a parameter, every failed rule named with its measurement; scripts without word spacing are judged by their letters |
| `MinHashSigner` | `Signatures/` | 128 MinHash values over 5-word shingles (FNV-1a + a seeded universal hash family), LSH band keys, estimated Jaccard between signatures |
| `CorpusCompressor` | `Compression/` | zstd through the managed ZstdSharp port at one pinned level, buffer and stream forms; the frame is a function of the content |
| `ShardProcessor` and friends | `Shards/` | the pipeline over one shard (`ShardProcessor`), the allowlist gate (`ShardOrigin`), the capped retrying fetch (`ShardFetcher`), the pinned model cache (`ShardLanguageModels`), the signatures layout (`ShardSignatureWriter`), the canonical counters (`ShardStatsBuilder`) and the node-side task start to finish (`ShardTaskRunner`) |
| `CorpusManifestParser` / `CorpusTaskPlanner` | `Manifests/` | the manifest document and the host-side plan |
| `CorpusReportWriter` | `Reports/` | the job report |
| `OutWit.Controller.AI.Corpus.Model` | | the wire types: source, pipeline, quality thresholds, task, result, statistics, options, preflight — MemoryPack with pinned member order |

`OutWit.Controller.AI.Corpus.Assets` (`corpus-assets`) trains and evaluates
the language model from WET files:

```
corpus-assets train-language-model <out.bin> <wet.gz>... [--profile N] [--min-docs N]
corpus-assets evaluate-language-model <model.bin> <wet.gz>
```

## Determinism

Every stage is managed code with no platform-dependent path: the same shard
through the same pipeline version gives the same bytes on Windows, Linux and
macOS. The tests pin a zstd frame digest and a MinHash signature digest,
compare extraction, identification and judging across runs, and prove the
content hash of a shard through the engine equals a direct run's; the
`AI Verify` workflow runs them on all three platforms.

## Tests

`OutWit.Controller.AI.Corpus.Tests`: every stage on the golden shards (the
first complete members of a real WET and a real WARC file, 1.5 MB each, and
the trained language model, committed under `Fixtures/`) and on hostile input
(truncation, oversized records, garbage, hostile nesting, repetition); the
pipeline's determinism and counters; the fetcher against a loopback origin
(retries, caps, final refusals, stalls); the node-side task's verdicts; the
manifest parser and planner; the report; the wire layout of every model type;
the hostile-input suite over synthetic shards (`WarcSynth` writes WARC/WET the
way Common Crawl does: truncation mid-record, invalid UTF-8, hostile HTML,
non-WARC records, corrupt gzip members); and the distributed gate — the bundled
scripts through the engine SDK with in-process nodes fetching from the loopback
origin. `Category=Load` (not run in CI): a 40k-document synthetic shard streams
through the processor in bounded memory to the same hash twice, and several
shards on one process agree with the single run.
