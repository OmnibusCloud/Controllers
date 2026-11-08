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

**Status: 0.1.0-dev — the CO-1 spike.** The streamed WARC/WET reader is in,
with golden shards from Common Crawl in the tests; the remaining stage
libraries (HTML to text, language identification, compression, MinHash)
follow, each with its determinism proven on the fixtures before the first
activity (`Corpus.ProcessShard`) is built on them.

## What is here

| Piece | Where | State |
|---|---|---|
| `WarcReader` / `WarcRecord` | `Warc/` | streams the records of a gzip (one member per record, as Common Crawl writes them) or plain WARC/WET file, one record in memory at a time; oversized records skipped and counted; a truncated tail ends the file with the complete records; malformed input is an error at an offset |

## Tests

`OutWit.Controller.AI.Corpus.Tests`: the reader on the golden shards (the
first complete members of a real WET and a real WARC file, 1.5 MB each,
committed under `Fixtures/`) and on hostile input (truncation, oversized
records, garbage, missing and bad lengths), plus the module skeleton.
