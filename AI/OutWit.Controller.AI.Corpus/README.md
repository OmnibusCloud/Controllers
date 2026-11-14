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

**Status: 0.2.0 — 0.1.14 proven on the test fleet; 0.2.0 (language packs with the
unsupported-language test, C4's curly-bracket rule as a switch) proven in CI, not yet on the
fleet; not yet released to nuget.org.**
The stage libraries are wired into nine activities and four bundled scripts; the
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
| `Corpus.Split` | host | `(Blob manifest, Blob languageModel, CorpusOptions) → CorpusTaskCollection` | parses the manifest, checks every source against the allowlist and the byte cap, hashes the language model blob and pins it into the pipeline, holds the server storage estimate to the deployment's headroom, emits one task per shard. Any problem fails the job here — a bad manifest never reaches a node |
| `Corpus.Preflight` | host | `(Blob manifest, Blob languageModel, CorpusOptions) → CorpusPreflight` | the same checks, reported instead of thrown: shard count, refused hosts, oversized sources, the traffic estimate (sources without a size are asked at the origin with HEAD, up to 64 of them, the rest extrapolated at the mean), the server storage estimate and the headroom it is held to (see *Server storage*), every message by manifest line |
| `Corpus.ProcessShard` | node | `(CorpusTask) → CorpusResult` | the allowlist re-check, the pinned model, the capped fetch, the pipeline, the uploads. Every way a shard can fail is a verdict in the result (`OriginRefused`, `FetchFailed`, `Oversized`, `Malformed`, `PipelineUnavailable`, `Failed`), never an exception; several shards may run on one client at once |
| `Corpus.Collect` | host | `(CorpusResultCollection, CorpusOptions) → Blob report` | the job report: every shard's verdict, blobs and content hash in task order, the aggregate counters over the fleet |
| `Corpus.SamplingRate` | host | `(CorpusOptions) → Double` | `SamplingRate` clamped to 0..1, the share of the shards `Grid.Replicas` runs twice |
| `Corpus.Dedup` | host | `(Blob report, CorpusOptions) → Blob dedup` | the near-duplicate merge over the signature blobs a report names: LSH over the MinHash signatures (16 bands of 8), a candidate pair joins a cluster at a Jaccard estimate of `DedupThreshold` (0.8 by default), the lowest document of a cluster is kept; per shard a keep mask, plus the clusters |
| `Corpus.Integrity` | host | `(CorpusResultCollection executions) → Blob integrity` | the two executions of every shard that ran twice compared on verdict, content hash, document counts and histograms |
| `Corpus.RuntimeDiagnostics` | host | `() → CorpusRuntimeDiagnostics` | what the module of the answering process carries (see *Diagnostics and capabilities*) |
| `Corpus.Capabilities` | host | `() → Blob capabilities` | what an application can know before a run, as a JSON document (`docs/corpus-capabilities.schema.json`) |

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
`Corpus.Dedup` over a finished run's report (`Job:CorpusDedup(Blob:report, CorpusOptions:opts)`);
`CorpusRuntimeDiagnostics.wit` takes nothing and returns `result` (the diagnostics) and
`capabilities` (the document). `languageModel` is the
blob of a language pack built by `corpus-assets` (see *Language packs*), or of a
0.1.x model; pass an empty blob when the pipeline's `identify_language` is `false`. `CorpusOptions`: `MaxShardBytes`
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
    "language_model_sha256": "74c6c348…",
    "target_languages": ["eng", "deu"],
    "identify_language": true,
    "clean_lines": true,
    "filter_quality": true,
    "sign": true,
    "filter_curly_bracket": false,
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
  must match the model blob or the job does not start. A language pack carries
  its languages' rule data and acceptance limits, so the pin covers them too.
- `allowed_origins` are host names, matched exactly and case-insensitively
  (ports and paths do not matter); a source whose host is not listed is
  refused on the host and, should it ever get there, on the node. The list
  narrows what a machine fetches from; it cannot widen it (see *Where a
  machine fetches from*).
- A bare URL's format follows its extension (`.warc.wet.gz` → WET,
  `.warc.gz` → WARC); an object may state `format`, a `size` estimate for the
  traffic line and the cap check, and the origin's published `sha256`, which
  the node verifies after the fetch.
- The language stage (`identify_language`) labels a document with a language
  of the job's language pack, or drops it: too little text to tell (fewer
  than 20 letters) as `language_unknown`, a language the pack does not
  support as `language_unsupported` (see *Language packs*), a supported
  language `target_languages` does not list as `language_not_target`.
- `clean_lines` (on unless the manifest turns it off) removes, before a
  document is judged, the lines that do not read like prose — the C4 line
  rules (Raffel et al. 2020), made script-aware: a line stays when it ends a
  sentence and has five words (six letters in a script without word
  spacing), or when it is long (twelve words, forty letters; C4 drops every
  line without a final mark, and about half of the long ones are prose);
  menus, prices, counters and footers go. A line that mentions JavaScript or
  the site's terms of use or privacy policy goes whatever its shape (C4), and
  so does a line that mentions cookies (ours: C4's phrases catch about half
  of the consent banners, and only in English) and a line with a word longer
  than 1,000 characters (C4). The judged text is the product, so a page
  whose article outlives its navigation is kept, clean. A document with no
  such line drops as `no_prose_lines`. WET documents keep their line
  structure on the way in (one block per line), which the line stage and the
  quality filter's line rules both rely on.
- The quality filter (`filter_quality`) is the Gopher rule set (Rae et al.
  2021) with every measure defined as DataTrove — the code behind FineWeb —
  defines it: a repeated n-gram counts from its second occurrence on, the
  most frequent n-gram is the one with the most occurrences, character
  shares are of the whole text, hash marks and ellipses are judged
  separately. A word is a whitespace-separated token; in a script without
  word spacing it is a letter (a Latin word inside such text stays one
  word), and Japanese and Chinese take FineWeb-2's repetition thresholds. A
  document needs two distinct stop words of its language (Gopher's eight
  for English, FineWeb-2's lists for the other languages of the reference
  pack: the pack's rule data), which removes pages in a language the
  identifier does not know that it labelled with one it does; a page with
  "lorem ipsum" goes (C4). The drop
  names are the rules': `min_words`, `max_words`, `mean_word_length`,
  `symbol_to_word_ratio`, `alphabetic_words`, `bullet_lines`,
  `ellipsis_lines`, `duplicate_lines`, `duplicate_line_chars`,
  `top_ngram_chars_2..4`, `duplicate_ngram_chars_5..10`, `stop_words`,
  `lorem_ipsum` — a document is counted under the first it fails, in that
  order.
- `filter_curly_bracket` (off unless the manifest turns it on) is C4's
  curly-bracket rule as C4 (TFDS `c4_utils.clean_page`) and DataTrove
  (`C4QualityFilter`, `filter_curly_bracket`) define it: walking the lines,
  a line with a word over 1,000 characters, a line without the shape of
  prose and a line that mentions JavaScript are passed over, and the first
  line left that holds `{` drops the document as `curly_bracket` — before
  the policy and cookie lines are looked at, whether or not the line stage
  is on. It catches code and template leftovers, and some prose that quotes
  a brace; FineWeb keeps it on (−2.8 % of its tokens). On a held-out Common
  Crawl WET file (00035, see *Language packs*) it drops 254 documents, and
  the documents out fall by 1.4 % (11,151 to 10,996), the text by 2.1 %.
  Whether a lab wants code-bearing pages is the lab's decision.
- Unknown keys are errors, so a typo cannot silently turn a stage off.

## Language packs

A language model is a job's input, pinned by `language_model_sha256`; the
controller's code holds no per-language table. A **language pack** (format
`WITLID2`, `Language/LanguageModelFormat.cs`) carries, as data, everything a
language needs:

- the character n-gram profiles (n = 1 to 4, 3,000 n-grams per language,
  Cavnar–Trenkle out-of-place distance) of the languages it **supports**, and
  of languages it **knows but does not support**;
- per supported language, its **acceptance limit**: the largest distance from
  its own profile a document may have, calibrated on labelled text;
- per supported language, its **rule data**: the script (ISO 15924; it
  decides whether the quality stages count words or letters), the stop words
  and how many a document must hold, and the quality thresholds that differ
  from the script's defaults (FineWeb-2's for Japanese and Chinese).

A document is labelled with its nearest profile only when that profile is a
supported language and the distance is inside the language's limit.
Otherwise the document is **unknown** and drops as `language_unsupported`,
so a report shows how much text a lab loses to languages outside the pack,
and nothing is labelled with a language it merely resembles. A Swedish page
finds the Swedish profile instead of the German one; a page in a language
the pack has no profile for at all is usually too far from every supported
profile. `language_unknown` keeps its meaning: too little text to tell.

A `WITLID1` model (0.1.x, profiles only) is still read and behaves exactly
as under 0.1.14: the nearest profile always wins, and the rule data is
0.1.14's, kept frozen as data (`Language/Resources/witlid1-rules.json`). A
manifest pinned to the old model's hash keeps producing the same bytes.

**The reference pack** — `Fixtures/language-pack-cc-2026-43-16.bin` in the
tests, the default of the tests and of `corpus-live`: `cc-2026-43-16`,
610,372 bytes, SHA-256
`74c6c348b897998ca947e286cd673f41d20e9efc3c242befc575a852d8d8dbd3`. All
input files are Common Crawl CC-MAIN-2026-43 WET files:

- **training** — the 16 supported languages from segment 1791170053823.21,
  files 00001 and 00002: the files of the 0.1.x model, so the 16 profiles are
  that model's to the n-gram, and the pack never labels a document
  differently from it, it can only say "unknown";
- **known languages** — the 23 languages with at least 100 documents in
  those two files and files 00020-00029 of the same segment: `ara aze ben bul
  cat dan ell est fin heb hrv hun ind kor lav lit nor ron slk slv srp swe
  tha`;
- **calibration** — files 00030-00034 of the same segment, at a recall of
  0.999 (a language's region holds 999 of 1,000 of its calibration
  documents);
- **rule data** — `OutWit.Controller.AI.Corpus.Assets/languages/`;
- **evaluation** — nine files used for nothing else: 00000 and 00035-00039
  of the same segment and file 00500 of segments 1791170054112.6,
  1791170054429.52 and 1791170055137.36.

On the evaluation files (128,699 documents labelled with a supported
language, 10,126 with another), the supported languages keep their label at
99.01 % (99.27 % without the test), and of the documents the line stage and
the quality filter keep, the test loses 0.07 %. Documents in other languages
become unknown at 98.5 %, and of those the quality stages would keep — the
mislabelled text in 0.1.14's products — at 98.1 % (by characters 94.6 %):
Swedish, Norwegian, Romanian, Hungarian and Bulgarian 100 %, Slovak 99.8 %,
Danish and Catalan 99.3 %. What gets through is a close relative the pack
has no profile for: Galician reads as Spanish, Belarusian as Ukrainian or
Russian, Afrikaans and Frisian as Dutch, Luxembourgish as German — each
fewer than 30 documents in nine files, too few to train a profile from the
files used. One held-out file (00035) under the default pipeline: 11,737
documents out with the 0.1.x model, 11,151 with the pack (−5.0 %; 2,559
`language_unsupported`), the text product 7.4 % smaller. Of the 586
documents no longer kept, 283 carry another language's label, 249 are mixed
pages whose main language is another, 48 mixed pages whose main language is
supported, 5 carry a supported language's label. The `stop_words` drops fall
from 1,042 to 169: that rule was catching foreign text the pack now names.
Identification against 39 profiles costs no more than against 16: the
profiles are searched through one index (507 µs per document; 0.1.14 took
557 µs with its 16).

## Adding a language

A language is data, not code: a folder of rule data, a pack built and
evaluated with `corpus-assets`, and a manifest that pins the new pack's
hash. No controller release.

1. **Text.** Common Crawl labels every WET record with the language its
   classifier identified (`WARC-Identified-Content-Language`), so any WET
   file is labelled text. A supported language needs 200 single-language
   documents in the training files (`--min-docs`) and 50 in the calibration
   files; a CC-MAIN-2026-43 file holds about 15,500 single-language
   documents, so a language with 1 % of them needs two training files, one
   with 0.1 % about thirteen. Keep training, calibration and evaluation
   files apart.
2. **Rule data**, a folder `languages/<code>/` named by the crawl's label
   (ISO 639-3):
   - `language.json` — `{"name": "Swedish", "script": "Latn"}`; `"quality"`
     with the thresholds the language has of its own (any of the manifest's
     ten names, `top_ngram_chars` with three values, `duplicate_ngram_chars`
     with six); `"min_stop_words"` (2 by default); `"notes"` saying where
     each value comes from;
   - `stopwords.txt` — one word per line, `#` comment lines, its source and
     licence in the header. FineWeb-2 publishes a stop-word list and tuned
     thresholds per language (`configs/<code>_<script>.yml`, Apache-2.0).
     Without a list the stop-word rule is off for the language.
3. **Build:**
   ```
   corpus-assets build-language-pack my-pack.bin --languages languages \
     --train <wet.gz>... [--known-train <wet.gz>...] --calibrate <wet.gz>... [--recall 0.999]
   ```
   Every language with a folder is supported; every other label with
   `--known-min-docs` (100) documents in the training and `--known-train`
   files becomes a known profile, its text unknown instead of mislabelled
   (rare languages need more files than the supported ones). The tool prints
   the documents per language, the limits and the pack's SHA-256, and the
   pack records its input files by name and hash. The same inputs give the
   same bytes.
4. **Evaluate** on files outside both sets:
   ```
   corpus-assets evaluate-language-pack my-pack.bin <wet.gz>...
   ```
   Per supported language: its documents, the recall without the
   unsupported test and with it, how many it called unsupported, too short
   or another supported language, and — `kept`, `kept lost` — of the
   documents the line stage and the quality filter keep, how many the test
   loses. Per other language: the share that becomes unknown and what the
   rest was labelled as; `(k)` marks a known profile. Then the confusion
   between supported languages and the documents with several labels. A
   supported language should keep about 99 % and lose well under 1 % of its
   kept documents; a known language should be about 99 % unknown. A close
   relative without a profile shows up "labelled as" the language it
   resembles: give `--known-train` more files until it has 100 documents.
5. **Pin it.** `"language_model_sha256": "<the pack's SHA-256>"` in the
   manifest, the pack as the job's `languageModel` blob (`corpus-live
   --model my-pack.bin`). A new pack is a new pipeline: change
   `pipeline.version` with it.

**What still needs a controller release** is what is code. The word: a
whitespace-separated token, or a letter in the scripts the controller knows
to be written without spaces (`LanguageScripts.UNSPACED`: Han, Japanese,
Thai, Lao, Khmer, Myanmar, Tibetan); a language in another script written
without spaces, or one that needs a real word segmenter to be judged well,
needs code. So does a new kind of quality rule (a threshold name the
controller does not know makes it refuse the pack, never ignore the value),
a change to the identification method, and the line stage's sentence ends:
it knows `. ! ? 。 ． ！ ？ ‼ ⁇ ؟ ۔ । ॥`, so in a script that ends sentences
otherwise (Ethiopic `።`, Burmese `။`, Armenian `։`, Thai with none) every
line is judged as unterminated and needs twelve words, or forty letters, to
stay.

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
  documents extracted and out, drops by rule, languages, bytes, times), and the
  platform (`runtime_target`, e.g. `linux-x64`) and degree it ran at — a
  platform, never a machine; the summary sums them over the fleet.

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
progress) is counted as `version_skew`, not compared. A pair with a side that
did not finish for a reason of its machine or its link — `FetchFailed`,
`PipelineUnavailable`, `Failed` — is counted as `incomplete`, not compared: a
mismatch says that two finished executions disagree, never that one of them
stopped. The mismatch rate is over the compared pairs. The report has one entry per shard: a completed execution,
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

## Server storage

The input does not touch the server; the products do. Every execution of a
shard uploads its text blob and its signatures blob, the job writes its report
and its integrity document, a dedup run writes its own, and the server keeps
all of it for the retention window (a product from its shard's completion, the
documents from the job's). For a job of terabytes that is the real ceiling of a
deployment, so the preflight estimates it and a deployment can set a limit.

**The estimate** (`EstimatedStorageBytes` in the preflight) is built from each
shard's size — the manifest's, or the origin's answer to HEAD — and ratios
measured on real runs, per format and per setting of `clean_lines` and
`filter_quality`; each is the largest value measured, raised by a fifth:

| Format | `clean_lines` | `filter_quality` | Text per byte fetched | Documents per MiB |
|---|---|---|---|---|
| WET | on | on | 0.38 | 240 |
| WET | on | off | 0.64 | 350 |
| WET | off | on | 0.55 | 200 |
| WET | off | off | 0.98 | 370 |
| WARC | on | on | 0.09 | 50 |
| WARC | on | off | 0.11 | 80 |
| WARC | off | on | 0.12 | 60 |
| WARC | off | off | 0.18 | 100 |

A document is 1,024 bytes of signatures (none when `sign` is off). The sampled
shards count twice — the largest ones, so the estimate does not depend on which
the platform picks — and the documents and the inputs (manifest, model) are
added. Twenty Common Crawl WET shards with every stage on (1,265 MiB fetched,
sampling 0.25) leave about 772 MiB under 0.1.14 (its products measured on one
machine; 653 MiB on the test server under 0.1.13); the estimate for them is 983 MiB.
A source whose size is unknown is not in the estimate, and the message says so.
The guard test over the golden shards fails when a stage makes products larger
than the table allows.

**The limit.** The engine says nothing about storage to a controller, so the
host asks two places and the smaller stated value holds:

- the server's blob service, by reflection: a public
  `Task<long> GetJobStorageHeadroomBytesAsync()` (meant to be backed by a server
  setting `BlobStorage:JobStorageHeadroomBytes`) and `int JobBlobRetentionDays`
  on it. A server without these members — every server so far — states nothing;
- the environment variable `OMNIBUSCLOUD_CORPUS_STORAGE_HEADROOM_BYTES` of the
  server process, in bytes: the operator's own word.

When the estimate exceeds the headroom, `Corpus.Preflight` answers
`WellFormed = false` with a message that names both numbers, and
`Corpus.Split` fails the job before a node is asked. A deployment that states
no headroom refuses nothing on storage; the preflight still reports the
estimate.

## Diagnostics and capabilities

`CorpusRuntimeDiagnostics.wit` answers for the server's module (a plain
script function runs on the host; the nodes run the same version, the platform
admits no other to the work):

- `result` (`CorpusRuntimeDiagnosticsData`): the controller version and
  platform; the stages in order and the stage set's fingerprint — the content
  hash of the benchmark's reference shard through the stages, computed there,
  with `StagesVerified` when it is the pinned one; the HTML parser and the
  compressor with their versions; the language-pack format (`WITLID2`; `WITLID1`
  models are still read) and the
  hash of the reference model the tests and fleet proofs use; the public
  origins and the hosts the server's operator lists; the shard cap, the
  downloads per node and the memory per shard.
- `capabilities` (`corpus-capabilities.json`,
  `docs/corpus-capabilities.schema.json`): the same plus what an application
  shows before a run — the formats, the defaults and the ceilings, every
  pipeline switch and quality threshold a manifest may set with its default
  (for scripts with and without word spacing), the thresholds it cannot set,
  the signatures and dedup parameters, the products with their schemas, the
  storage ratios with the deployment's headroom and retention window, and the
  pool as totals: nodes, measured nodes, MB of WET per second (and shards at
  once when the host carries every node's degree). No node is named.

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
- The language pack (or model) arrives as a blob (the engine caches it per node) and is
  trusted only when its bytes hash to the pipeline's pin; a model that does
  not match is `PipelineUnavailable`, not a different result. A transfer over
  the blob channel that fails — the model coming in, a product going out — is
  tried again for about five minutes, so a link that drops for a while (a
  server restart, a reconnect) does not become a failed shard; a transfer
  that fails in the end says how many times and for how long it was tried.
  The first transfer of a node goes alone and the rest wait for it (the
  channel is connected by its first user; shards that start together would
  each begin a connection of their own), and retries are spread at random so
  that transfers that failed together do not come back together.
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
- **The last share.** A run is split once and sent once: each node gets
  its shards by its benchmark rate, a sampled shard's copy goes to another
  machine, and nothing is handed out again while the run goes well. So a run
  ends when its slowest share ends. A node slower than its benchmark said, or
  busy with its owner's work, holds the run until it finishes; a node that
  goes silent loses its share after the silence window (90 s by default), and
  the whole share is sent to another node and processed again from the start.
  With a benchmark that represents the work and machines that are really free
  the difference is small; on a fleet of desktops it is the price of sending
  everything once.
- **Benchmark.** `Corpus.ProcessShard` measures a node on a synthetic
  reference shard (400 documents generated from a fixed seed, menus and
  short pages among them), one copy per slot of the node's degree at once;
  the product's hash is checked, so a node that computes wrong cannot score.
  The rate is megabytes of real Common Crawl WET per second
  (`corpus-wet-mb@synthetic-v2`), the unit of the work estimate — a task's
  shard size, which `Corpus.Split` asks the origin for when the manifest does
  not give it — so the allocator gives a 32-thread desktop the share its
  speed earns.

## What is here

| Piece | Where | What it does |
|---|---|---|
| `WarcReader` / `WarcRecord` | `Warc/` | streams the records of a gzip (one member per record, as Common Crawl writes them) or plain WARC/WET file, one record in memory at a time; oversized records skipped and counted; a truncated tail ends the file with the complete records; malformed input is an error at an offset |
| `HttpResponsePayload` / `HtmlTextExtractor` | `Text/` | a WARC response's HTTP headers and body, decoded by header charset → meta charset → UTF-8; HTML to text on AngleSharp with a scoped rule set (scripts, styles, navigation, forms and hidden content dropped; blocks become lines; title first); a linear pre-scan refuses pages nested deeper than 512 or past 60k tags. Agrees with Common Crawl's own WET text at a median Jaccard of 0.85 on the golden shard |
| `LanguageIdentifier` / `LanguageModel` / `LanguageModelFormat` / `LanguageAcceptance` / `LanguageRules` / `LanguageRuleSet` | `Language/` | character n-gram profiles (Cavnar–Trenkle, integer rank distance, ordinal ties) and the unsupported-language test; the two model formats (`WITLID1` profiles only, `WITLID2` language packs) with one canonical serialization each; each language's rule data (script, stop words, thresholds) as data, and the frozen rule data of `WITLID1` models (`Resources/witlid1-rules.json`); `LanguageScripts`, the scripts written without spaces — the one piece of language knowledge in code |
| `LanguageModelTrainer` / `LanguagePackBuilder` / `LanguagePackCalibrator` / `LanguagePackEvaluator` / `LanguagePackSource` | `Language/` | what `corpus-assets` runs: profiles trained from WET files' own language labels, the per-language source folders read, the limits calibrated, a pack measured against the crawl's labels |
| `LineCleaner` / `QualityFilter` / `QualityRules` / `QualityRuleOverrides` | `Quality/` | the line stage (C4's line rules, script-aware; C4's curly-bracket rule) and the Gopher quality and repetition rules with DataTrove's definitions of every measure, each threshold with its source in the code (Gopher, C4, FineWeb-2), every failed rule named with its measurement; scripts without word spacing are judged by their letters; the thresholds a language's rule data may set, by name |
| `MinHashSigner` | `Signatures/` | 128 MinHash values over 5-word shingles (FNV-1a + a seeded universal hash family), LSH band keys, estimated Jaccard between signatures |
| `CorpusCompressor` | `Compression/` | zstd through the managed ZstdSharp port at one pinned level, buffer and stream forms; the frame is a function of the content |
| `ShardProcessor` and friends | `Shards/` | the pipeline over one shard (`ShardProcessor`), the allowlist gate (`ShardOrigin`), the capped retrying fetch (`ShardFetcher`), the pinned model cache (`ShardLanguageModels`), the signatures layout (`ShardSignatureWriter`), the canonical counters (`ShardStatsBuilder`) and the node-side task start to finish (`ShardTaskRunner`) |
| `CorpusManifestParser` / `CorpusTaskPlanner` | `Manifests/` | the manifest document and the host-side plan |
| `CorpusReportWriter` | `Reports/` | the job report |
| `CorpusStorageEstimator` and friends | `Storage/` | the server storage estimate (`CorpusStorageEstimator` over the measured `CorpusStorageRatios`), the deployment's limit behind `ICorpusStorageHeadroom` (`CorpusStorageHeadroomHost`: the server's blob service, the operator's variable) and the refusal (`CorpusStorageCheck`) |
| `CorpusRuntimeDiagnosticsBuilder` / `CorpusCapabilitiesWriter` | `Diagnostics/` | the module's diagnostics, the stage set and its fingerprint (`CorpusStageSet`), the reference language pack's facts, the pool as totals (`CorpusPoolProbe`) and the capabilities document |
| `OutWit.Controller.AI.Corpus.Model` | | the wire types: source, pipeline, quality thresholds, task, result, statistics, options, preflight, runtime diagnostics — MemoryPack with pinned member order |

`OutWit.Controller.AI.Corpus.Assets` (`corpus-assets`) builds and evaluates
language packs from the per-language source data in its `languages/` folder
and WET files (see *Adding a language*), and still trains and evaluates the
profiles-only `WITLID1` model:

```
corpus-assets build-language-pack <out.bin> --languages <dir> --train <wet.gz>... [--known-train <wet.gz>...]
              --calibrate <wet.gz>... [--recall 0.999] [--name NAME] [--profile 3000] [--min-docs 200] [--known-min-docs 100] [--threads N]
corpus-assets evaluate-language-pack <pack.bin> <wet.gz>... [--no-judge] [--threads N]
corpus-assets train-language-model <out.bin> <wet.gz>... [--profile N] [--min-docs N]
corpus-assets evaluate-language-model <model.bin> <wet.gz>
```

The stop-word lists in `languages/` are Gopher's eight English words and
FineWeb-2's lists (Apache-2.0, `languages/LICENSE-fineweb-2.txt`), each file
naming its source.

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
first complete members of a real WET and a real WARC file, 1.5 MB each, the
reference language pack and the 0.1.x model, committed under `Fixtures/`) and on
hostile input
(truncation, oversized records, garbage, hostile nesting, repetition); the
pipeline's determinism and counters; the language packs (the format and its
refusals, the unsupported-language test on sentences written for the tests, the
0.1.x model's products pinned unchanged, the build and evaluation on a small
generated fixture, the frozen 0.1.14 rule data); the fetcher against a loopback origin
(retries, caps, final refusals, stalls); the node-side task's verdicts; the
manifest parser and planner; the report; the wire layout of every model type;
the storage estimate against a measured run and the golden shards, the
headroom sources and the refusal; the diagnostics and the capabilities
document, with every published schema checked against what the writers write;
the hostile-input suite over synthetic shards (`WarcSynth` writes WARC/WET the
way Common Crawl does: truncation mid-record, invalid UTF-8, hostile HTML,
non-WARC records, corrupt gzip members); and the distributed gate — the bundled
scripts through the engine SDK with in-process nodes fetching from the loopback
origin. `Category=Load` (not run in CI): a 40k-document synthetic shard streams
through the processor in bounded memory to the same hash twice, and several
shards on one process agree with the single run.
