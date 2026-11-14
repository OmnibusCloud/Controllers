# 01 · Common Crawl: the expected result

Measured locally with Corpus 0.3.0: the 24 files of `manifest.json` (their SHA-256 checked against
the manifest) through the stage pipeline of the controller, the manifest's pipeline and the
reference language pack `cc-2026-43-16`. A shard's product is a function of its bytes and the
pipeline, so these numbers are the fleet's: the same content hashes and counters on any node and
platform. What only a fleet can measure is marked *fleet*, to be filled in from the first walk.

## The report

```
report     : corpus-report/2, controller 0.3.0, output stats_and_hashes
shards     : 24 (Completed 24)
fetched    : 1,584,054,229 bytes; the product would be 448,706,065 bytes compressed
funnel     : records 515,379 > extracted 515,355 > language 453,528 > lines 427,293 > quality 273,891 = out 273,891
dropped    : extract 0, language 61,827, lines 26,235, quality 153,402
drops      : min_words 63,219, language_unsupported 58,953, no_prose_lines 26,235, duplicate_ngram_chars_5 20,251, alphabetic_words 14,169, duplicate_lines 13,712, ellipsis_lines 11,753, duplicate_line_chars 8,762
languages  : eng 128,566, rus 20,244, deu 18,794, spa 17,019, fra 16,052, jpn 13,963, zho 13,294, ita 8,826, por 8,696, pol 6,368, nld 5,987, tur 4,051, ces 3,450, vie 3,386, ukr 2,870, fas 2,325
not kept   : ind 7,111, past_limit:eng 5,357, kor 4,188, swe 3,898, ara 3,444, ron 3,337, ell 2,884, dan 2,836, hun 2,678, hrv 2,399 (+30 more)
identity   : c4b6253029af5202747e284bdeca9c368ee57593eff27c3017f9eb50d9f4aa72
shards the kit does not expect: 0
```

- The 24 records that are not documents are the files' `warcinfo` records; a WET file has no
  extraction drops.
- `language_unknown` (fewer than 20 letters) is 2,874 of the language stage's 61,827 drops,
  `language_unsupported` 58,953 across 40 places in the breakdown; no document is dropped as
  `language_not_target`: the manifest keeps every supported language.
- Per shard: 11,151 to 11,624 documents out of 21,039 to 21,826; every content hash is in
  `shards.tsv`. The identity line is the SHA-256 of the lines `index:content_sha256` in index
  order: one number for the whole run.
- `bytes_produced` is what the text product measured on the nodes before it was deleted with the
  task: 28.3 % of the bytes fetched.

## The preflight

| | Value |
|---|---|
| Shards | 24, well-formed |
| Traffic | 1,584,054,229 bytes (the manifest's sizes; the shards sampled for integrity are fetched once more by their second machine: about 0.4 GB at `--sampling 0.25`) |
| Server storage | 702,300 bytes at `--sampling 0.25`: the report and the integrity document (84,992), the manifest and the pack (617,308). The same run with `"output": "full"`: 1,231,056,545 bytes (1.15 GiB) |
| Notes | `output stats_and_hashes: … Corpus.Dedup cannot run over this run` and the storage line `no text and no signatures (output stats_and_hashes: they stay on the nodes)` |
| Busiest node's share, nodes planned | *fleet* (a percentage and a count) |

## The run

| | Run 1 | Run 2 |
|---|---|---|
| Verdicts | Completed 24 | the same |
| Content hashes | the 24 of `shards.tsv`, identity `c4b62530…f9eb50d9f4aa72` | the same |
| Text and signature blobs | none | none |
| Integrity at `--sampling 0.25` | 6 shards run twice, *fleet*: 6 compared, 0 mismatches expected | the same |
| Wall time | *fleet* (about 50 s of processing at the test fleet's 30.6 MB/s of WET, plus the fetch) | *fleet* |
| `corpus-live dedup` | refused: "The run kept statistics and hashes only (pipeline output "stats_and_hashes"): it has no signatures, …" | — |

## The same run in full

With `"output": "full"` the content hashes and every counter above are the same, and the run
keeps 449 MB of text and 280 MB of signatures. The near-duplicate merge over the 24 shards'
signatures (the defaults: 128 hashes over 5-word shingles, 16 bands, threshold 0.8): 273,891
documents, **18,610 near-duplicates** (6.8 %) in 8,858 clusters, the largest of 193 documents.

Why the studio shows no "dedup so far" during a run: a node sees its own shard only, and its
shard's near-duplicates are a small part of the run's: 2,764 found within the shards one by one,
15 % of the 18,610 the merge finds. The merge over the first shards grows much faster than the
shards do:

| Shards merged | Documents | Near-duplicates | Clusters | Largest |
|---|---|---|---|---|
| 1 | 11,508 | 125 | 89 | 9 |
| 4 | 45,588 | 1,210 | 838 | 27 |
| 8 | 91,413 | 3,781 | 2,382 | 62 |
| 12 | 137,374 | 7,292 | 4,189 | 97 |
| 16 | 182,899 | 11,481 | 6,074 | 131 |
| 20 | 228,758 | 16,077 | 7,901 | 160 |
| 24 | 273,891 | 18,610 | 8,858 | 193 |

## What may differ on another fleet

- The wall time, which shards the sampling picks, the busiest share, and the platforms and
  degrees in the report (`runtime_target`, `degree`, `fetch_ms`, `process_ms`, `wait_ms`).
- Nothing else: the content hashes, the counters and the funnel are the same on any fleet. They
  change only when a stage changes, that is, with a controller release that says so, or with
  another language pack.
