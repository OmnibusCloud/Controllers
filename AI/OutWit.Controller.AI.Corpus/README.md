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
opting in, a rule about where a machine fetches from that a job cannot widen
(see *Where a machine fetches from*), retries with jitter towards the origin.

**Status: 0.1.5 — proven on the test fleet, not yet released to nuget.org.**
The stage libraries are wired into seven activities and three bundled scripts; the
engine rig in the tests fetches the golden shards from a loopback origin
through mock nodes and reproduces a direct run's content hashes. On the test
server the controller processed real Common Crawl WET shards on Windows and
Linux nodes with every sampled pair agreeing on the content hash (0
mismatches), and the dedup merge found the near-duplicates across twenty
shards' signatures in seconds. What remains before 1.0: the quality thresholds tuned on real
crawl text, and the dedup merge measured at the scale of a crawl segment.

## Activities

| Activity | Runs on | Signature | What it does |
|---|---|---|---|
| `Corpus.Split` | host | `(Blob manifest, Blob languageModel, CorpusOptions) → CorpusTaskCollection` | parses the manifest, checks every source against the allowlist and the byte cap, hashes the language model blob and pins it into the pipeline, emits one task per shard. Any problem fails the job here — a bad manifest never reaches a node |
| `Corpus.Preflight` | host | `(Blob manifest, Blob languageModel, CorpusOptions) → CorpusPreflight` | the same checks, reported instead of thrown: shard count, refused hosts, oversized sources, the traffic estimate (sources without a size are asked at the origin with HEAD, up to 64 of them, the rest extrapolated at the mean), every message by manifest line |
| `Corpus.ProcessShard` | node | `(CorpusTask) → CorpusResult` | the allowlist re-check, the pinned model, the capped fetch, the pipeline, the uploads. Every way a shard can fail is a verdict in the result (`OriginRefused`, `FetchFailed`, `Oversized`, `Malformed`, `PipelineUnavailable`, `Failed`), never an exception; several shards may run on one client at once |
| `Corpus.Collect` | host | `(CorpusResultCollection, CorpusOptions) → Blob report` | the job report: every shard's verdict, blobs and content hash in task order, the aggregate counters over the fleet |
| `Corpus.SamplingRate` | host | `(CorpusOptions) → Double` | `SamplingRate` clamped to 0..1, the share of the shards `Grid.Replicas` runs twice |
| `Corpus.Dedup` | host | `(Blob report, CorpusOptions) → Blob dedup` | the near-duplicate merge over the signature blobs a report names: LSH over the MinHash signatures (16 bands of 8), a candidate pair joins a cluster at a Jaccard estimate of `DedupThreshold` (0.8 by default), the lowest document of a cluster is kept; per shard a keep mask, plus the clusters |
| `Corpus.Integrity` | host | `(CorpusResultCollection executions) → Blob integrity` | the two executions of every shard that ran twice compared on verdict, content hash, document counts and histograms |

## Scripts

```
Job:CorpusProcess(Blob:manifest, Blob:languageModel, CorpusOptions:opts)
{
    CorpusTaskCollection:tasks = Corpus.Split(manifest, languageModel, opts);

    Double:rate = Corpus.SamplingRate(opts);
    ProcessingOptions:checked = Grid.Replicas(2, rate);

    CorpusResultCollection:executions = Grid.ForEach(task in tasks, checked)
        => Corpus.ProcessShard(task);

    Blob:report = Corpus.Collect(executions, opts);
    Blob:integrity = Corpus.Integrity(executions);
}
```

`CorpusPreflight.wit` runs `Corpus.Preflight` alone; `CorpusDedup.wit` runs
`Corpus.Dedup` over a finished run's report (`Job:CorpusDedup(Blob:report, CorpusOptions:opts)`). `languageModel` is the
blob of a model trained by `corpus-assets` (see below); pass an empty blob when
the pipeline's `identify_language` is `false`. `CorpusOptions`: `MaxShardBytes`
(0 = the controller's cap of 1 GiB; a smaller value lowers it), `SamplingRate`
(the share of shards re-executed for integrity, 0 to 1; 0 by default),
`DedupThreshold` (0 = 0.8) and `FetchConcurrency` (reserved; the node's own
ceiling is two downloads at once).

## The manifest

One JSON document (`docs/corpus-manifest.schema.json`):

```json
{
  "pipeline": {
    "version": "cc-text-v1",
    "language_model_sha256": "9670e262…",
    "target_languages": ["eng", "deu"],
    "identify_language": true,
    "clean_lines": true,
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
  refused on the host and, should it ever get there, on the node. The list
  narrows what a machine fetches from; it cannot widen it (see *Where a
  machine fetches from*).
- A bare URL's format follows its extension (`.warc.wet.gz` → WET,
  `.warc.gz` → WARC); an object may state `format`, a `size` estimate for the
  traffic line and the cap check, and the origin's published `sha256`, which
  the node verifies after the fetch.
- `clean_lines` (on unless the manifest turns it off) removes, before a
  document is judged, the lines that do not read like prose: a line stays
  when it ends a sentence and has three words (six letters in a script
  without word spacing), or when it is long (twelve words, forty letters);
  menus, prices, counters and footers go. The judged text is the product, so
  a page whose article outlives its navigation is kept, clean. A document
  with no such line drops as `no_prose_lines`. WET documents keep their line
  structure on the way in (one block per line), which the line stage and the
  quality filter's line rules both rely on.
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
- **Dedup** (`corpus-dedup.json`, `docs/corpus-dedup.schema.json`): per shard
  a keep mask over the product (a base64 bitset, bit *i* = the *i*-th JSON line,
  1 = keep) and the clusters of two or more as (shard, ordinal) pairs with the
  kept document first; a lab assembles the corpus by reading each text blob
  and skipping the lines whose bit is 0.
- **Report** (`corpus-report.json`, `docs/corpus-report.schema.json`): per
  shard the verdict, blob ids, content hash, error and statistics (records in,
  documents extracted and out, drops by rule, languages, bytes, times); the
  summary sums them over the fleet.

## Integrity

`SamplingRate` runs a share of the shards twice, in the same `Grid.ForEach` call
as the rest, and `Corpus.Integrity` writes `corpus-integrity.json`
(`docs/corpus-integrity.schema.json`): for every shard that ran twice its two
executions compared on everything an honest node
cannot vary — the verdict, the content hash of the product, the record and
document counts, the drop and language histograms. Times and bytes are not
compared. The platform picks the shards at random and keeps the two executions
on different machines, so a node cannot tell whether its shard is being checked.
No document says which machine ran what.
With a single machine available there is nothing to compare and no shard runs
twice. A pair whose sides ran different controller versions (a rollout in
progress) is counted as `version_skew`, not compared; the mismatch rate is over
the compared pairs. The report has one entry per shard: a completed execution,
then the lowest content hash.

## Where a machine fetches from

A task says which URL to fetch, and a task is written by whoever submitted
the job. So the task's word is not enough: without a rule of the machine's
own, a job could send a contributor's computer to any address, the ones
inside that contributor's network included, and read the answer back as
"cleaned text". Every request therefore passes two questions, on a node and
on the server alike (the server asks origins for shard sizes):

1. **Does the task name the host?** The manifest's `allowed_origins`.
2. **Does this machine fetch from the host at all?** Not something a
   manifest can answer.

A machine fetches from two kinds of host:

- **The public origins compiled into the controller**: `data.commoncrawl.org`
  today. Over HTTPS, on the default port, and only at public internet
  addresses — a name that resolves into a private, loopback or link-local
  range is not contacted, and the address that was checked is the address
  that is connected to.
- **Hosts the machine's owner listed** in the environment variable
  `OMNIBUSCLOUD_CORPUS_ORIGINS` (host names separated by `;`, `,` or spaces)
  of the process that runs the controller. This is the owner's own decision
  about the owner's own machine, so such a host is taken as written: HTTP or
  HTTPS, any port, any address. A lab's data server inside its network is the
  case; on the public network nobody needs to set it.

Redirects are never followed blindly: each hop is put to both questions, at
most five hops. A URL with a user name or password is refused.

What a job sees when a host is not a public origin: `Corpus.Preflight` and
`Corpus.Split` still plan the shards and name the host in a message, because
the answer belongs to each machine. A machine whose owner listed the host
processes the shard; any other answers `OriginRefused` without sending a
request. The server does not ask such a host for sizes unless its own
operator listed it, so the traffic estimate for those shards comes from the
manifest's `size` values.

## Node behaviour

- The node fetches only what the task declares and the machine permits:
  the URL's host must be on the task's allowlist and pass the machine's own
  rule above, the scheme HTTP(S); nothing else is ever requested, a redirect
  included.
- The fetch is streamed to the task's own temp scope and capped: a
  `Content-Length` past the cap ends the request before a byte is read, a
  streamed body past it is cut. Three attempts with doubling back-off and
  jitter on connection failures, 429 and 5xx; a stalled read (60 s without a
  byte) counts as a failure; a 4xx other than 429 is final. A retry after a
  broken body asks for the rest with a Range request and appends when the
  origin honours it. At most two shards download at once per node, whatever
  the node's degree; the others wait their turn. The user agent names the
  controller and its version.
- The language model arrives as a blob (the engine caches it per node) and is
  trusted only when its bytes hash to the pipeline's pin; a model that does
  not match is `PipelineUnavailable`, not a different result.
- Processing is one streaming pass: one record in memory at a time, the
  product hashed and compressed as it is written, on a thread below normal
  priority (a worker is often a desktop). At most one shard per core
  processes at once, fewer when memory is short (256 MB budgeted per shard);
  the others of a batch wait their turn, already fetched. The task's temp
  scope is deleted when the task ends, whatever the verdict.
- **Progress.** The node reports its share, not one shard, about twice a
  second through the host's activity progress sink: first the delivery
  (`receiving shards: 3/12, 512.3/1888.0 MB; 1/12 processed`), then the work
  (`7/12 shards processed`). Delivery stands for a fifth of the share's
  progress, processing for the rest.
- **Benchmark.** `Corpus.ProcessShard` measures a node on a synthetic
  reference shard (400 documents generated from a fixed seed, menus and
  short pages among them), one copy per slot of the node's degree at once;
  the product's hash is checked, so a node that computes wrong cannot score.
  The rate is megabytes of real Common Crawl WET per second
  (`corpus-wet-mb@synthetic-v1`), the unit of the work estimate — a task's
  shard size, which `Corpus.Split` asks the origin for when the manifest does
  not give it — so the allocator gives a 32-thread desktop the share its
  speed earns.

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
through the same pipeline and the same controller version gives the same bytes
on Windows, Linux and macOS, on one thread or many. A controller release that
changes a stage changes the product of an unchanged pipeline (0.1.5 keeps WET
line structure and adds the line stage), which is why the integrity document
counts a pair whose sides ran different controller versions as `version_skew`
instead of comparing it. The tests pin a zstd frame digest and a MinHash signature digest,
compare extraction, identification and judging across runs, and prove the
content hash of a shard through the engine equals a direct run's; the
`AI Verify` workflow runs them on all three platforms.

## Performance

One streaming pass costs about 0.75 s of one core per megabyte of Common
Crawl WET on a desktop CPU (Ryzen 9 5950X, every stage on); the stages count
their n-grams over word ids and packed 64-bit keys, so nothing allocates per
n-gram or per shingle and many shards on one machine do not stall each other
in the garbage collector. A node runs one shard per core at most.

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
