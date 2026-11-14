# 01 · Common Crawl: statistics and hashes only

Twenty-four public Common Crawl WET files of the crawl CC-MAIN-2026-43 (1.58 GB on the wire, about
half a million web pages) go through the full pipeline on the fleet: language identification
against the reference language pack, the line stage, the quality filter. The run keeps
**statistics and hashes only** (`"output": "stats_and_hashes"`): every node processes, signs and
hashes its shards in full and returns the counters and the hashes of the product and of its
signatures, but no text and no signature leaves a node and nothing third-party is stored on the
server. That is the shape of a showcase
run and of a lab that may not hold the text: the numbers, and a hash that a later full run
reproduces.

## Files

- `manifest.json`: the pipeline (the reference pack pinned by its hash, every stage at its
  default, `"output": "stats_and_hashes"`) and the 24 shards with their sizes and SHA-256, so
  the traffic line is exact and a node that receives other bytes says `FetchFailed` instead of
  computing on them. Shards 0 to 19 are files 00020 to 00039 of segment 1791170053823.21 (the test
  fleet's acceptance set), shard 20 file 00000 of the same segment, shards 21 to 23 file 00500 of
  segments 1791170054112.6, 1791170054429.52 and 1791170055137.36.
- `shards.tsv`: per shard its file, size and input hash, and what the kit expects: the content
  hash, the signatures hash and the counts. Hashes and counts only.
- `check.py`: turns a saved report into the funnel and the language table and checks every shard
  against `shards.tsv`.

## Run

From `AI/OutWit.Controller.AI.Corpus.Live`, with a `wit_sk_` key in `WITAI_VERIFY_LIVE_KEY`:

```
dotnet build -c Release
bin/Release/net10.0/corpus-live preflight demo/01-common-crawl/manifest.json --sampling 0.25
bin/Release/net10.0/corpus-live process demo/01-common-crawl/manifest.json --runs 2 --sampling 0.25 --save demo-out/cc
python demo/01-common-crawl/check.py demo-out/cc/report-1.json demo-out/cc/integrity-1.json
bin/Release/net10.0/corpus-live dedup demo-out/cc/report-1.json
```

`--model` is left out: the tool sends the reference pack it carries, the one the manifest pins.

## What to say, what to look at

1. **Before the run (preflight).** The two promises a corpus run makes: the contributors' traffic
   (1,993,119,664 bytes: every shard once, 1,584,054,229, each under the 1 GiB cap, and the six
   largest a second time for integrity, 409,065,435) and the server storage, which for this
   run is the report, the integrity document and the inputs: about 0.7 MB instead of the 1.15 GiB
   the same run would leave with its text. The line "output stats_and_hashes: … Corpus.Dedup
   cannot run over this run" says why. The work split line gives the busiest node's share of the
   work at this fleet, as a percentage: no node is named.
2. **The run.** Each node fetches its shards straight from Common Crawl; the progress shows the
   delivery, then the shards processed. A quarter of the shards run a second time on another
   machine.
3. **The report.** `check.py` prints the funnel: half a million records, 515,355 documents, 61,827
   leave at the language stage (58,953 of them in languages the pack does not support: Indonesian,
   Korean, Swedish, Arabic, Romanian lead), 26,235 have no line of prose, 153,402 fail the quality
   rules, and 273,891 documents in 16 languages would be the corpus: 449 MB of compressed text that
   was never kept. Every `text_blob` and `signatures_blob` is empty.
4. **The identity.** `shards the kit does not expect: 0`: every shard's content hash and
   signatures hash are the ones in `shards.tsv`, measured on another machine. The two runs give the
   same hashes (`content : all 2 run(s) give the same content hashes`), and the integrity line
   (`corpus-integrity/4`) counts the sampled pairs that agree across machines, on the text and on
   the signatures (`signatures compared 6, signature mismatches 0`).
5. **Dedup is refused**, by name: the run kept no signatures. The same manifest with
   `"output": "full"` gives the same 24 content hashes, the text, the signatures, and a merge
   that finds 18,610 near-duplicates in 8,858 clusters.

`EXPECTED.md` has the numbers.
