# 01 · Common Crawl: the expected result

Measured locally with Corpus 0.3.0 and measured again with 0.4.0, with 0.5.0 and with 0.6.0, which give the
same 24 content hashes, signatures hashes and counters (the stages' diagnostics test
`DemoKitPinsDiagnosticsTests` runs the files against `shards.tsv`); 0.7.0 and 0.8.0 change no stage, and their
stage set's fingerprint is the one of those; 0.9.0 adds limits to the stages (a document judged in full up to
100,000 words, a ceiling on a shard's product, 4,096 PDF runs a shard) that no file of the kit comes near, so
its numbers are these too, under another fingerprint; 0.9.1 and 1.0.0 leave the stage set as it is, and so does
1.0.1, which is the code of 1.0.0 (the source of the sandbox host it compiles has two additions the controller
does not use): nothing here was measured again with it. The 24 files of `manifest.json` (their SHA-256 checked
against the manifest) through the stage pipeline of the controller, the manifest's pipeline and
the reference language pack `cc-2026-43-16`. A shard's product is a function of its bytes and the
pipeline, so these numbers are the fleet's: the same content hashes and counters on any node and
platform. What only a fleet can measure, and what differs from one fleet to another, is marked *fleet*.

On a macOS arm64 node, with every shard of the kit on two machines and the other copy on a Linux x64
node (Corpus 0.9.1, the code of 1.0.0), every shard's product came from the arm64 node and was the
x64 copy's: every shard `Completed`, with the content hash and the signatures hash `shards.tsv`
holds for it.

## The report

```
report     : corpus-report/6, controller 1.0.1, output stats_and_hashes
shards     : 24 (Completed 24)
fetched    : 1,584,054,229 bytes; the product would be 448,706,065 bytes compressed
funnel     : records 515,379 > extracted 515,355 > language 453,528 > lines 427,293 > quality 273,891 = out 273,891
dropped    : extract 0, language 61,827, lines 26,235, quality 153,402
drops      : min_words 63,219, language_unsupported 58,953, no_prose_lines 26,235, duplicate_ngram_chars_5 20,251, alphabetic_words 14,169, duplicate_lines 13,712, ellipsis_lines 11,753, duplicate_line_chars 8,762
languages  : eng 128,566, rus 20,244, deu 18,794, spa 17,019, fra 16,052, jpn 13,963, zho 13,294, ita 8,826, por 8,696, pol 6,368, nld 5,987, tur 4,051, ces 3,450, vie 3,386, ukr 2,870, fas 2,325
not kept   : ind 7,111, past_limit:eng 5,357, kor 4,188, swe 3,898, ara 3,444, ron 3,337, ell 2,884, dan 2,836, hun 2,678, hrv 2,399 (+30 more)
trust      : 18 shard(s) rest on one execution; rejected results 0, repairs 0, disputes 0 (resolved 0)
sampling   : as_asked (rate 0.25: 6 of 24 package(s) asked to run twice, 6 answered twice, 6 shard(s) compared)
identity   : c4b6253029af5202747e284bdeca9c368ee57593eff27c3017f9eb50d9f4aa72
signatures : f661f7a925d0160d190009d78673f7a27befb111b2841e00473dd36fcfd4fbb7
shards the kit does not expect: 0
```

- The 24 records that are not documents are the files' `warcinfo` records; a WET file has no
  extraction drops.
- `language_unknown` (fewer than 20 letters) is 2,874 of the language stage's 61,827 drops,
  `language_unsupported` 58,953 across 40 places in the breakdown; no document is dropped as
  `language_not_target`: the manifest keeps every supported language.
- Per shard: 11,151 to 11,624 documents out of 21,039 to 21,826; every content hash and every
  signatures hash is in `shards.tsv`. The identity line is the SHA-256 of the lines
  `index:content_sha256` in index order, the signatures line the same over
  `index:signatures_sha256`: one number each for the whole run. The signatures (128 hashes over
  5-word shingles) are made and hashed on the nodes and never leave them.
- `bytes_produced` is what the text product measured on the nodes before it was deleted with the
  task: 28.3 % of the bytes fetched.
- The `trust` line at `--sampling 0.25`: the 18 shards the sampling did not pick rest on one
  machine's word; a run that goes well drops, repairs and disputes nothing. (Without sampling the
  line reads 24.)
- The `sampling` line at `--sampling 0.25`, on a pool of two machines or more: a quarter of the
  24 packages is 6, and every one of them came back from its second machine. `short` or `none`
  in the place of `as_asked` means that checking the run asked for did not happen (a pool of one
  machine gives `none`); without sampling the line reads `not_asked`.

## The preflight

| | Value |
|---|---|
| Shards | 24, well-formed |
| Traffic at `--sampling 0.25` | 2,192,485,391 bytes: fetches 1,993,119,664 (every shard once, 1,584,054,229, the manifest's sizes, and the second fetch of the 6 largest shards, sampled for integrity, 409,065,435), uploads 53,760 (the result packages: under statistics and hashes only nothing else leaves a node) and an allowance of 199,311,967 (a tenth of the fetches). The line, when the server does not know its pool: `Contributor traffic estimate 2192485391 bytes: fetches 1993119664 (every shard once 1584054229 and the second fetch of the 6 largest shard(s), sampled for integrity, 409065435), uploads 53760 (the products and the result packages of every execution) and an allowance of 199311967 (a tenth of the fetches) for fetches that start over and for shards fetched again in a repair or a dispute.` With a pool the language pack (610,372 bytes) is added once for each node the plan uses, and the line names it |
| What the nodes enforce | 2,242,259,608 bytes for those fetches at the most: every shard is held to the size the manifest declares and an eighth more (a line of its own: `The nodes accept at most … bytes for those fetches, whoever wrote the manifest: …`). The origin is asked about a random sample of the sizes first (`Sizes asked at the origin for a random sample: … 24 declared size(s) checked.`) |
| What the traffic line leaves out | `Not in the traffic estimate: a node that fails or falls silent has its whole share fetched again by other nodes. The allowance is not a bound: a shard whose two copies disagree is fetched again by up to 3 machines, and by up to 3 more when they leave it unsettled (a dispute and its second round; 6 shard(s) are sampled here), and a shard a node returns as a fault of its own is fetched again by 2 machines (a repair); the report's counters (repairs, disputes) say what a run added. The number of nodes is not known here: every node that takes part also downloads the language model once (610372 bytes).` |
| Node disk | 457,609,025 bytes free for each shard a node holds (the largest shard, 69,422,286 bytes, at the most a node fetches it as: 78,100,071 bytes with the eighth of slack; the most its products may be under the product ceiling: 156,200,142 bytes of text and 156,199,948 of signatures; and 64 MiB), reserved before the shard is fetched; a node holds its degree and 2 more at a time: 4,576,090,250 bytes for a node of degree 8 |
| Server storage | 783,708 bytes at `--sampling 0.25`: the report and the integrity document (84,992), the task and result packages (81,408), the manifest and the pack (617,308). The same run with `"output": "full"`: 1,231,137,953 bytes (1.15 GiB) |
| Notes | `output stats_and_hashes: … Corpus.Dedup cannot run over this run` and the storage line `no text and no signatures (output stats_and_hashes: they stay on the nodes)` |
| Busiest node's share, nodes planned | *fleet* (a percentage and a count) |

## The run

| | Run 1 | Run 2 |
|---|---|---|
| Verdicts | Completed 24 | the same |
| Content hashes | the 24 of `shards.tsv`, identity `c4b62530…f9eb50d9f4aa72` | the same |
| Signatures hashes | the 24 of `shards.tsv`, identity `f661f7a9…cfd4fbb7` | the same |
| Text and signature blobs | none (the signatures are made and hashed on the nodes: their hashes are in `shards.tsv`) | none |
| Integrity at `--sampling 0.25` (`corpus-integrity/8`) | 6 shards run twice, *fleet*: 6 compared, 6 with their signatures compared, 0 mismatches expected; `sampling.state` `as_asked` (6 asked twice, 6 answered twice), no package unanswered in any round; rejected results 0, node faults 0, repairs 0, no result 0, disputes 0 expected of an honest fleet with a working link | the same |
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
  times in the report (`runtime_target`, `fetch_ms`, `process_ms`, `wait_ms`).
- A node that fails on a shard (its disk, its link, an upload): the shard is sent out once more
  (to two machines when the run samples) and the counters `node_faults` and `repairs` say so; the
  content hashes do not move.
- Nothing else: the content hashes, the counters and the funnel are the same on any fleet. They
  change only when a stage changes, that is, with a controller release that says so, or with
  another language pack.
