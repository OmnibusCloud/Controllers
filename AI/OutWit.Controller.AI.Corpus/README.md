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
fetch concurrency ceilings and jitter towards the origin.

**Status: 0.1.0-dev — the CO-1 spike: the stage libraries.** Every stage a
shard goes through exists as a library with its determinism proven on golden
shards from Common Crawl; the first activity (`Corpus.ProcessShard`) is built
on them next (CO-2).

## What is here

| Piece | Where | What it does |
|---|---|---|
| `WarcReader` / `WarcRecord` | `Warc/` | streams the records of a gzip (one member per record, as Common Crawl writes them) or plain WARC/WET file, one record in memory at a time; oversized records skipped and counted; a truncated tail ends the file with the complete records; malformed input is an error at an offset |
| `HttpResponsePayload` / `HtmlTextExtractor` | `Text/` | a WARC response's HTTP headers and body, decoded by header charset → meta charset → UTF-8; HTML to text on AngleSharp with a scoped rule set (scripts, styles, navigation, forms and hidden content dropped; blocks become lines; title first); a linear pre-scan refuses pages nested deeper than 512 or past 60k tags. Agrees with Common Crawl's own WET text at a median Jaccard of 0.85 on the golden shard |
| `LanguageIdentifier` / `LanguageModel` / `LanguageModelTrainer` | `Language/` | character n-gram profiles (Cavnar–Trenkle, integer rank distance, ordinal ties); the model is trained by `corpus-assets` from WET files' own language labels and pinned by hash; the committed 16-language model agrees with the crawl's labels at 98.8 % on the shard |
| `QualityFilter` / `QualityRules` | `Quality/` | the Gopher-style shape and repetition rules, every threshold a parameter, every failed rule named with its measurement; scripts without word spacing are judged by their letters |
| `MinHashSigner` | `Signatures/` | 128 MinHash values over 5-word shingles (FNV-1a + a seeded universal hash family), LSH band keys, estimated Jaccard between signatures |
| `CorpusCompressor` | `Compression/` | zstd through the managed ZstdSharp port at one pinned level, buffer and stream forms; the frame is a function of the content |

`OutWit.Controller.AI.Corpus.Assets` (`corpus-assets`) trains and evaluates
the language model from WET files.

## Determinism

Every stage is managed code with no platform-dependent path: the same shard
through the same pipeline version gives the same bytes on Windows, Linux and
macOS. The tests pin a zstd frame digest and a MinHash signature digest and
compare extraction, identification and judging across runs; the `AI Verify`
workflow runs them on all three platforms.

## Tests

`OutWit.Controller.AI.Corpus.Tests`: every stage on the golden shards (the
first complete members of a real WET and a real WARC file, 1.5 MB each, and
the trained language model, committed under `Fixtures/`) and on hostile input
(truncation, oversized records, garbage, hostile nesting, repetition), plus
the module skeleton.
