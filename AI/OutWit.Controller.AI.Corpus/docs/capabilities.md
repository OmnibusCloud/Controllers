# Corpus: what the controller can do

A reference for building on the Corpus controller. It covers:

- how to call it, and what goes in and what comes out;
- every stage, switch and threshold, every way a shard or a document can end;
- what is guaranteed about the results, and how a run is checked;
- what a lab sees, what never leaves a node, and where the edges are.

Every number on this page is a constant of the code or was measured, in the controller's tests
or on a test fleet; the [README](../README.md) explains how things work inside, and the JSON
schemas in this folder are the documents' contracts.

Version described: **Corpus 0.3.0** (Model 0.1.5, Scripts 0.1.3). Corpus 0.2.1 is proven on a
six-node test fleet (Windows, Linux, macOS); 0.3.0 adds what an application's studio needs
(sections 4, 6 and 8) and is proven in CI on the three platforms, not yet on the fleet. Nothing is
on nuget.org or on the live network yet.

## Contents

1. [What Corpus is for](#1-what-corpus-is-for)
2. [At a glance](#2-at-a-glance)
3. [Calling Corpus from an application](#3-calling-corpus-from-an-application)
4. [The manifest](#4-the-manifest)
5. [The stages](#5-the-stages)
6. [What comes back](#6-what-comes-back)
7. [Verdicts and drop reasons](#7-verdicts-and-drop-reasons)
8. [Before a run: preflight and storage](#8-before-a-run-preflight-and-storage)
9. [Determinism and integrity](#9-determinism-and-integrity)
10. [Scale, delivery and the nodes](#10-scale-delivery-and-the-nodes)
11. [Limits and ceilings](#11-limits-and-ceilings)
12. [What a lab sees and what never leaves the node](#12-what-a-lab-sees-and-what-never-leaves-the-node)
13. [Demo kit](#13-demo-kit)
14. [Versions and what a deployment carries](#14-versions-and-what-a-deployment-carries)
15. [Known limits](#15-known-limits)

## 1. What Corpus is for

Corpus turns public web data into training-grade text on anonymous computers. A job names
shards of a public crawl (Common Crawl first); each node fetches its shards straight from the
public origin, runs them through a fixed chain of deterministic stages and returns the product.
The server carries descriptors and results only, never the input.

| Product | What a lab gets |
|---|---|
| Cleaned text (B1/B4) | per shard, the documents that passed every stage as JSON lines `{"url","lang","text"}`, language-tagged, zstd-compressed |
| Near-duplicate map (B2) | per shard a MinHash signature per document, and over a run a keep mask per shard and the clusters |
| Statistics and hashes only | the same run without the text: every shard's counters and the hashes of the product and the signatures it would have kept, the run's funnel (for demonstrations and licensing) |

## 2. At a glance

| | |
|---|---|
| Input | Common Crawl WET and WARC shards (gzip), fetched by the node from `data.commoncrawl.org`, at most 1 GiB each |
| Stages | read → extract (HTML, and PDF in a wasm sandbox) → language identification → line cleaning → quality filter → compress → sign; the PDF, language, line, quality and signature stages each switchable |
| Languages | a language pack, pinned by hash: the reference pack supports 16 languages and knows 23 more, which it labels unknown instead of mislabelling them; a language is data, not code |
| Output | text and signatures per shard, or statistics and hashes only (`"output": "stats_and_hashes"`) |
| Determinism | the same shard through the same pipeline gives the same bytes on every node and platform |
| Integrity | a share of the shards runs twice on two different machines and the pairs are compared |
| Speed | 0.4 to 0.75 s of one core per MB of WET on a desktop CPU, every stage on; a six-node test fleet measures 30.6 MB of WET per second |
| Platforms | nodes on Windows x64, Linux x64 and macOS arm64, giving the same products |

## 3. Calling Corpus from an application

### The flow

Every input is a blob and every output is a blob. The application:

1. uploads the manifest (JSON, section 4) and the language pack;
2. optionally runs `CorpusPreflight` and shows the answer (section 8);
3. submits `CorpusProcess` with the two blob ids and the options;
4. waits for the `report` and the `integrity` documents and downloads them, then the text and
   signature blobs the report names;
5. optionally runs `CorpusDedup` over the report.

With the platform SDK (`OutWit.Cloud.SDK`; the options type is in
`OutWit.Controller.AI.Corpus.Model`):

```csharp
await using var client = new WitCloudClient(serverUrl, identityUrl, apiKey);   // a wit_sk_ application key
await client.ConnectAsync();

var manifest = await client.Blobs.UploadBlobAsync(File.ReadAllBytes("manifest.json"), "manifest.json");
var pack = await client.Blobs.UploadBlobAsync(File.ReadAllBytes("language-pack.bin"), "language-pack.bin");
var options = new CorpusOptionsData { SamplingRate = 0.25 };

var handle = await client.Scripts.SubmitAsync("CorpusProcess", [manifest, pack, options]);

var report = await handle.WaitAsync<Guid?>("report");        // also "integrity"
if (report.Status == ProcessingJobStatus.Completed)
{
    var bytes = await client.Blobs.DownloadBlobAsync(report.Result!.Value);
    // the report, JSON: section 6
}
```

`AI/OutWit.Controller.AI.Corpus.Live/Program.cs` (the `corpus-live` tool) is a complete, working
client: preflight, process (several runs, comparing their content hashes), dedup and diagnostics,
saving every document.

### The scripts

Bundled in `OutWit.Controller.AI.Corpus.Scripts`; they run on the server, and nodes run
`Corpus.ProcessShard`.

| Script | Inputs | Outputs | Use it for |
|---|---|---|---|
| `CorpusProcess` | manifest blob, language model blob, options | `report`, `integrity` | a run |
| `CorpusPreflight` | manifest blob, language model blob, options | `result` (`CorpusPreflightData`) | checking a manifest and stating the promises before a run (section 8) |
| `CorpusDedup` | report blob, options | `dedup` | the near-duplicate merge over a finished run |
| `CorpusRuntimeDiagnostics` | none | `result` (`CorpusRuntimeDiagnosticsData`), `capabilities` | what the deployment offers |

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

Pass an empty blob (`Guid.Empty`) as `languageModel` when the pipeline's `identify_language` is
`false`.

### The activities

| Activity | Runs on | What it does |
|---|---|---|
| `Corpus.Split` | server | parses the manifest, checks every source against the origin rule and the byte cap, pins the language pack's hash, refuses a pack the controller cannot use, asks the origin for unknown shard sizes, holds the storage estimate to the deployment's headroom, emits one task per shard. Any problem fails the job here: a bad manifest never reaches a node |
| `Corpus.Preflight` | server | the same checks, reported instead of thrown (section 8) |
| `Corpus.ProcessShard` | node | the shard start to finish: the origin re-check, the pinned pack, the capped fetch, the stages, the uploads (none under statistics and hashes only) |
| `Corpus.SamplingRate` | server | the options' `SamplingRate` clamped to 0..1 |
| `Corpus.Collect` | server | the report |
| `Corpus.Integrity` | server | the integrity document |
| `Corpus.Dedup` | server | the near-duplicate merge |
| `Corpus.RuntimeDiagnostics` | server | the module's diagnostics |
| `Corpus.Capabilities` | server | the capabilities document |

### The options (`CorpusOptionsData`)

| Field | Default | Meaning |
|---|---|---|
| `MaxShardBytes` | 0 = the controller's cap, 1 GiB | the most bytes a shard may be on the wire; a smaller value lowers the cap, a larger one is held to 1 GiB |
| `SamplingRate` | 0 | the share of the shards run twice on another machine for integrity, 0 to 1 (rounded up to whole shards) |
| `DedupThreshold` | 0 = 0.8 | the Jaccard estimate two documents need to be near-duplicates in `Corpus.Dedup` |
| `FetchConcurrency` | — | reserved; a node fetches at most two shards at once whatever it says |

### Before a run

`CorpusPreflight` answers without fetching a shard (section 8). `CorpusRuntimeDiagnostics` returns
the module's diagnostics and the `capabilities` document (schema
[`corpus-capabilities.schema.json`](corpus-capabilities.schema.json), `corpus-capabilities/1`):

| Field | What it holds |
|---|---|
| `controller`, `benchmark_unit` | the controller version; the unit of the nodes' benchmark rate |
| `stages` | the stages in order, the stage set's fingerprint and whether the answering process computes it, the language-pack format |
| `parsers` | the WARC reader, the HTML parser and the compressor with their versions |
| `language_model` | the formats read, the method, the reference pack's name, hash, supported and known languages |
| `formats`, `origins` | `wet`, `warc`; the public origins and the environment variable a machine's owner lists more hosts in |
| `defaults`, `ceilings` | section 11 |
| `pipeline` | every switch with its default, `output` with its values, `minhash` with its defaults and ranges, every quality threshold for scripts with and without word spacing, the thresholds a manifest cannot set |
| `pdf` | the PDF stage: the module's hash, the engine, the limits, the running-line rule, the drop rules, whether the host can run it |
| `signatures` | the default MinHash parameters and the bytes of one document's signature |
| `drop_rules` | every drop rule with the stage that applies it, in the order a document meets them |
| `products` | each product's file, schema and the output modes that keep it |
| `storage` | the measured ratios of the storage estimate, the deployment's headroom and retention window when stated |
| `pool` | the fleet as totals: `nodes`, `measured`, `workers` (when every measured node reports its degree), `rate` (MB of WET per second). No node is ever named |

### While it runs

A node reports its share about twice a second through the host's progress sink: first the delivery
(`receiving shards: 3/12, 512.3/1888.0 MB; 1/12 processed`), then the work (`7/12 shards
processed`). Delivery stands for a fifth of the share's progress. The counters and the funnel come
with the report, at the end; a near-duplicate figure comes from `CorpusDedup` after the run
(section 6).

## 4. The manifest

One JSON document (schema [`corpus-manifest.schema.json`](corpus-manifest.schema.json)). Unknown
keys are errors, so a typo cannot silently turn a stage off.

```json
{
  "pipeline": {
    "version": "cc-text-v1",
    "language_model_sha256": "74c6c348b897998ca947e286cd673f41d20e9efc3c242befc575a852d8d8dbd3",
    "target_languages": ["eng", "deu"],
    "identify_language": true,
    "clean_lines": true,
    "filter_quality": true,
    "filter_curly_bracket": false,
    "extract_pdf": true,
    "sign": true,
    "quality": { "min_words": 50, "max_duplicate_line_ratio": 0.3 },
    "minhash": { "hashes": 128, "shingle_words": 5, "bands": 16 },
    "output": "full"
  },
  "allowed_origins": ["data.commoncrawl.org"],
  "sources": [
    "https://data.commoncrawl.org/crawl-data/CC-MAIN-2026-43/segments/…/wet/CC-MAIN-…-00000.warc.wet.gz",
    { "url": "https://data.commoncrawl.org/…/warc/CC-MAIN-…-00000.warc.gz", "format": "warc", "size": 1073741824, "sha256": "…" }
  ]
}
```

### The pipeline

| Key | Default | What it does |
|---|---|---|
| `version` | required | the lab's label for the contract; it travels with every task and does not enter the products' bytes. Change a threshold, change the version |
| `language_model_sha256` | none | the pack's pin: when given, a pack with other bytes does not start the job. It covers the profiles, the limits and every language's rule data |
| `target_languages` | all | ISO 639-3 codes to keep; another supported language drops as `language_not_target`. Ignored (with a note) when the language stage is off |
| `identify_language` | on | the language stage (section 5); needs a language model blob |
| `clean_lines` | on | the line stage |
| `filter_quality` | on | the quality filter |
| `filter_curly_bracket` | off | C4's curly-bracket rule (section 5) |
| `extract_pdf` | on | the PDF stage on WARC shards; off, a PDF drops as `not_html` |
| `sign` | on | a MinHash signature per document |
| `quality` | the stage defaults | the thresholds below; a key left out, or 0, is the default |
| `minhash` | 128 / 5 / 16 | the signatures and the merge: `hashes` 1 to 1,024, `shingle_words` 1 to 32, `bands` a divisor of `hashes`; a key left out, or 0, is the default |
| `output` | `"full"` | `"full"` keeps every shard's text and signatures on the server; `"stats_and_hashes"` keeps the statistics, the content hashes, the report and the integrity document only |

The quality thresholds a manifest may set (the defaults are Gopher's for scripts with word
spacing, and for scripts without it, where a word is a letter; a language pack may carry a
language's own):

| Key | Spaced | Unspaced | Rule |
|---|---|---|---|
| `min_words` | 50 | 100 | `min_words` |
| `max_words` | 100,000 | 1,000,000 | `max_words` |
| `min_mean_word_length` | 3 | 0 (off) | `mean_word_length` |
| `max_mean_word_length` | 10 | none (`null` in the capabilities) | `mean_word_length` |
| `max_symbol_to_word_ratio` | 0.1 | 0.1 | `symbol_to_word_ratio` (hash marks, and separately ellipses, per word) |
| `max_bullet_line_ratio` | 0.9 | 0.9 | `bullet_lines` |
| `max_ellipsis_line_ratio` | 0.3 | 0.3 | `ellipsis_lines` |
| `min_alphabetic_word_ratio` | 0.8 | 0 (off) | `alphabetic_words` |
| `max_duplicate_line_ratio` | 0.3 | 0.287 | `duplicate_lines` |
| `max_duplicate_line_char_ratio` | 0.2 | 0.2 | `duplicate_line_chars` |

Fixed, not settable from a manifest (a language pack may set them per language): the most
characters the top word n-gram may cover, n = 2, 3, 4: 0.20, 0.18, 0.16 (unspaced 0.256, 0.201,
0.171); the most characters repeated n-grams may cover, n = 5 to 10: 0.15, 0.14, 0.13, 0.12,
0.11, 0.10 (unspaced 0.198, 0.182, 0.167, 0.154, 0.14, 0.127); two distinct stop words of the
document's language, from the pack's rule data.

**Statistics and hashes only.** Every shard is processed, signed and hashed exactly as under
`"full"`: the same content hashes, the same signatures hashes, the same counters, the same
`bytes_produced`. Its text and signatures are written, hashed and measured on the node, and
nothing is uploaded; the task's folder, with the products in it, is deleted when the task ends.
The sampled shards still run twice and both hashes are still compared across machines. The
storage estimate drops the products (section 8). The report names no blob, and `Corpus.Dedup`
refuses the run: there are no signatures to merge. A lab can therefore show and check what a run
would produce without holding any third-party text: a later run with `"full"` gives the same
hashes.

**MinHash parameters.** The defaults sign byte for byte as Corpus 0.2.x did. The hash family is
one sequence, so a signature of *n* hashes is the first *n* values of a longer one. A value out of
its range is refused by name (`pipeline.minhash: 'bands' (12) must divide 'hashes' (128): …`).
Signatures made with different parameters cannot be compared: the blob, the result and the report
state how they were made, and `Corpus.Dedup` refuses to mix them. With `sign` off the parameters
are ignored, with a note.

### The origins and the sources

- `allowed_origins`: host names, matched exactly and case-insensitively (ports and paths do not
  matter). A source whose host is not listed is refused on the server and, should it get there,
  on the node. The list narrows what a machine fetches from and cannot widen it (section 12).
- `sources`: a URL string, whose extension tells the format (`.warc.wet.gz` is WET, `.warc.gz`
  is WARC), or an object with `url`, `format` (`wet` or `warc`), `size` (bytes on the wire: the
  traffic line, the cap check and the work estimate) and `sha256` (the expected hash: the node
  checks the fetched bytes and answers `FetchFailed` when they differ). A URL listed twice is two
  tasks.

## 5. The stages

The stages in the order a document meets them (`stages.order` in the capabilities): `read`,
`extract`, `extract_pdf`, `identify_language`, `clean_lines`, `filter_quality`, `compress`,
`sign`. A drop is counted under the first rule a document fails; each rule belongs to one stage
(`drop_rules` in the capabilities).

**Read.** A gzip WARC or WET file (one member per record, as Common Crawl writes them) is
streamed one record at a time. A record over 64 MiB is skipped and counted; a truncated tail ends
the file with the complete records (the result's `error` says so, the verdict stays
`Completed`); bytes that are not a WARC file are `Malformed`.

**Extract.** A WET conversion record is a document as it is, its line structure kept. A WARC
response needs status 200 and an HTML body: the HTTP headers and body are parsed, the body decoded
by the header's charset, then the meta charset, then UTF-8, and turned into text on AngleSharp
(scripts, styles, navigation, forms and hidden content dropped; blocks become lines; the title
first). A page nested deeper than 512 elements or with more than 60,000 tags is refused
(`html_refused`) by a linear pre-scan; a page longer than 4 Mi characters is cut there before
parsing. On the golden shard the extracted
text agrees with Common Crawl's own WET text at a median Jaccard of 0.85.

**PDFs (WARC only, `extract_pdf`).** A response with status 200 whose `Content-Type` or
`WARC-Identified-Payload-Type` is `application/pdf` and whose body starts with `%PDF-` is run
through `pdftext` (pdf_oxide 0.3.78 built for wasm32-wasip1, SHA-256 `13abba93…b5f6`) in the
controller's own wasmtime sandbox: it sees only its input file and an empty output folder, has no
network, a pinned clock and seeded randomness.

| Limit | Value |
|---|---|
| fuel | 8·10¹⁰ per PDF, counted from `_start` (given after instantiation, so it does not depend on the platform) |
| linear memory | 1 GiB |
| input | 64 MiB (a PDF Common Crawl truncated at 5 MiB is read all the same, and counted) |
| text | 16 MiB of UTF-8 |
| wall clock | 10 minutes, a backstop only: a run that reaches it is a fault of the node, never a verdict |
| document | at most 5,000 words: pages are put together while they fit; a longer page is a document of its own, cut after its last whole line |

The text is shaped: pages split at form feeds, whitespace collapsed, running headers and footers
removed (a line that, with every run of digits folded, appears on half the pages or more of a PDF
of five pages or more), and Cyrillic or Greek words broken at a line end joined again. Each
page-range document is named `url#page=a-b` and goes through the stages below like a page of HTML.
Every limit, the module's pin and the shaping rules are part of the stage set's fingerprint. A
node that cannot run the sandbox makes no product of a shard that holds a PDF: the shard fails
there as a fault of the node and runs on another; a shard without a PDF runs on it as on any other.

**Language identification (`identify_language`).** Character n-gram profiles (n = 1 to 4,
3,000 n-grams per language, ranked distance after Cavnar and Trenkle) over a document's first
4,000 characters. A document with fewer than 20 letters drops as `language_unknown`. Otherwise
it is labelled with its nearest profile when that is a supported language and the distance is
inside the language's calibrated limit; else it drops as `language_unsupported`, and the report's
`unsupported_languages` says where it was nearest (a known language's code, `past_limit:<code>`,
or `no_profile`). A supported language that `target_languages` does not list drops as
`language_not_target`.

**Language packs.** The model is a job input, pinned by hash. A pack (`WITLID2`) carries the
profiles of the languages it supports and of languages it knows but does not support, each
supported language's acceptance limit, and its rule data: the script (it decides whether the
quality stages count words or letters), the stop words with the number a document must hold (2
by default), and the thresholds that differ from the script's defaults. The reference pack
`cc-2026-43-16` (610,372 bytes, SHA-256 `74c6c348…8dbd3`) supports `ces deu eng fas fra ita jpn
nld pol por rus spa tur ukr vie zho` and knows `ara aze ben bul cat dan ell est fin heb hrv hun ind
kor lav lit nor ron slk slv srp swe tha`; on nine held-out files the supported languages keep
their label at 99.01 % and documents in other languages become unknown at 98.5 %. A `WITLID1`
model (0.1.x) is still read and gives the products it gave then.

**Adding a language** is a data step, no controller release: a folder `languages/<code>/` with
`language.json` (name, script, its own thresholds, stop-word count) and `stopwords.txt`; then
`corpus-assets build-language-pack` from labelled Common Crawl WET files (200 single-language
documents to train, 50 to calibrate), `corpus-assets evaluate-language-pack` on files outside
both sets, and the new pack's hash in the manifest. What still needs a release: a script written
without spaces that the controller does not know (`Han, Japanese, Thai, Lao, Khmer, Myanmar,
Tibetan` are known), a new kind of quality rule, sentence ends the line stage does not know, a
change to the identification method. The README's *Adding a language* has the commands.

**Lines (`clean_lines`).** C4's line rules (Raffel et al. 2020), script-aware: a line stays when
it ends a sentence (`. ! ? 。 ． ！ ？ ‼ ⁇ ؟ ۔ । ॥`) and has 5 words (6 letters in a script without
word spacing), or when it is long: 12 words (40 letters). A line that mentions JavaScript, terms of
use, a privacy or cookie policy, or cookies at all, and a line with a word over 1,000 characters,
go. A document with no line left drops as `no_prose_lines`; the cleaned text is the product.
`filter_curly_bracket` (off by default) is C4's rule as C4 and DataTrove define it: the first line
C4 would test that holds `{` drops the document as `curly_bracket`, before the line stage, whether
or not the line stage is on.

**Quality (`filter_quality`).** The Gopher rules (Rae et al. 2021) with every measure as
DataTrove defines it, in this order: `min_words`, `max_words`, `mean_word_length`,
`symbol_to_word_ratio`, `alphabetic_words`, `bullet_lines`, `ellipsis_lines`, `duplicate_lines`,
`duplicate_line_chars`, `top_ngram_chars_2..4`, `duplicate_ngram_chars_5..10`, `stop_words`,
`lorem_ipsum` (C4). Japanese and Chinese take FineWeb-2's repetition thresholds from the reference
pack's rule data.

**Compress.** zstd at level 9 through the managed ZstdSharp port; the frame is a function of the
content.

**Sign (`sign`).** Per document a MinHash signature: the text's tokens (runs of letters and
digits, lower-cased by the invariant culture), `shingle_words` of them in a row, each shingle hashed
once (64-bit FNV-1a over UTF-8), and `hashes` universal hash functions (multiply-add-shift, seeds
from one constant through splitmix64) each keeping its minimum.

## 6. What comes back

| Product | Per | Kept under | Schema |
|---|---|---|---|
| `text.jsonl.zst` | shard execution | `full` | JSON lines, below |
| `signatures.bin` | shard execution | `full` | binary, below |
| `corpus-report.json` | run | both | [`corpus-report.schema.json`](corpus-report.schema.json), `corpus-report/2` |
| `corpus-integrity.json` | run | both | [`corpus-integrity.schema.json`](corpus-integrity.schema.json), `corpus-integrity/4` |
| `corpus-dedup.json` | dedup run | `full` | [`corpus-dedup.schema.json`](corpus-dedup.schema.json), `corpus-dedup/1` |
| `corpus-capabilities.json` | diagnostics run | — | [`corpus-capabilities.schema.json`](corpus-capabilities.schema.json), `corpus-capabilities/1` |

**Text.** zstd-compressed JSON lines, one per document that passed every stage,
`{"url","lang","text"}` in that order; `lang` is "" when the language stage is off; a PDF's
document carries `#page=a-b` in its `url`. The result's `content_sha256` is the SHA-256 of the
*uncompressed* lines: the identity a re-execution must reproduce.

**Signatures.** The magic `WITSIG1\n`, the hash count (int32 LE), then one signature per document
in product order (uint64 LE each): document *i* at `12 + i × 8 × hashes`. With a hash count or a
shingle size other than 128 and 5, the magic is `WITSIG2\n` and the shingle size (int32 LE)
follows the hash count: document *i* at `16 + i × 8 × hashes`. The SHA-256 of the blob's bytes,
kept or not, is the signatures' identity (`signatures_sha256`), which integrity compares.

**The report.** One entry per shard in task order (a shard that ran twice: a completed execution,
then the lowest content hash) with `index`, `url`, `verdict`, `text_blob`, `signatures_blob`,
`content_sha256`, `error`, `controller`, `runtime_target` (a platform such as `linux-x64`, never a
machine), `degree`, `minhash` when signatures were made, `signatures_sha256` ("" when none was
made), and `stats`: `records_in`,
`documents_extracted`, `documents_out`, `bytes_fetched`, `bytes_produced` (the compressed product,
kept or not), `fetch_ms`, `process_ms`, `wait_ms`, `drops`, `languages`, `unsupported_languages`,
and the PDF counters where the shard met a PDF (`pdf_in`, `pdf_truncated`, `pdf_extracted`,
`pdf_pages`, `pdf_documents`, `pdf_pages_cut`, `pdf_running_lines`, `pdf_fuel`). The `summary`
sums them over the completed shards and adds:

- `verdicts`: shards per verdict;
- `funnel`: `records`, `extracted`, `language` (left after the language stage), `lines` (after the
  line stage), `quality` (after the quality filter), `out` (equal to `quality`), and `dropped` per
  stage: `extract` (records and PDFs that gave no document, beside the funnel), `language`,
  `lines`, `quality`. `extracted − dropped.language − dropped.lines − dropped.quality = out`;
- `output`: `full` or `stats_and_hashes`;
- `minhash`: the parameters of the run's signatures, when the shards have any.

The funnel needs no second pass and no new counter: it is the drop histogram summed by stage, so
an application can draw the same funnel for one shard from its `drops` with the capabilities'
`drop_rules`.

**The integrity document.** Section 9.

**The dedup document.** `CorpusDedup` reads the report and the signature blobs it names. A
candidate pair is found by LSH (`bands` bands of `hashes / bands` rows; 16 bands of 8 catch a
0.8-similar pair with probability 0.97 and a 0.5-similar one with 0.06) and joins a cluster at a
Jaccard estimate of `DedupThreshold` (0.8); the lowest document of a cluster, in (shard, ordinal)
order, is kept. The `summary` gives `threshold`, `bands`, `hashes`, `shingle_words`, `shards`,
`documents`, `duplicates`, `kept`, `clusters`, `clusters_listed`; per shard a keep mask (`keep`,
base64, bit *i* of byte *i/8*, least significant first, 1 = keep the *i*-th JSON line); and the
clusters of two or more as `[shard, ordinal]` pairs, the kept document first, listed up to
100,000 (the masks carry the whole decision). A lab assembles the corpus by reading each text blob
and skipping the lines whose bit is 0. Refused by name: a run that kept statistics and hashes
only, a run whose completed shards have documents and no signatures (`sign` off), shards whose
signatures were made with different MinHash parameters, and a blob whose header disagrees with the
report. On the test fleet the merge over twenty shards' signatures (203,462 documents) took
seconds. A figure "so far" during a run is not offered: the server holds no shard's signatures
before `Grid.ForEach` returns, and a shard's own near-duplicates are a small part of the run's
(section 13 has the numbers).

## 7. Verdicts and drop reasons

A shard ends in one verdict (the report's `verdict`):

| Verdict | When | Integrity |
|---|---|---|
| `Completed` | fetched and processed; under `full` its products uploaded | compared |
| `OriginRefused` | the host is not on the task's list or not one this machine fetches from; nothing was requested | compared |
| `FetchFailed` | the origin could not be fetched after the retries, or the bytes did not hash to the manifest's `sha256` | not compared (`incomplete`) |
| `Oversized` | the shard grew past the cap while fetched; nothing kept | compared |
| `Malformed` | the bytes are not a WARC file | compared |
| `PipelineUnavailable` | the pinned pack did not arrive or does not match its pin | not compared (`incomplete`) |
| `Failed` | anything else went wrong on the node; `error` names it | not compared (`incomplete`) |

A fault of the node that another node would not have (a PDF it cannot run, a product it cannot
upload) is not a verdict: the node's batch fails and `Grid.ForEach` gives its tasks to another
node.

A document that does not reach the product is counted under one rule:

| Stage | Rules |
|---|---|
| extract | `http_unparsable`, `http_status`, `not_html`, `html_refused`, `empty_text`, `pdf_encrypted` (a password is needed), `pdf_unreadable`, `pdf_no_pages`, `pdf_fuel` (out of fuel), `pdf_memory` (a refused allocation), `pdf_trap` (the module crashed), `pdf_error` (any other failure, or output that breaks the module's contract), `pdf_oversized` (more than 16 MiB of text), `pdf_empty` (text, but no line once shaped) |
| language | `language_unknown` (fewer than 20 letters), `language_unsupported`, `language_not_target` |
| lines | `curly_bracket`, `no_prose_lines` |
| quality | `min_words`, `max_words`, `mean_word_length`, `symbol_to_word_ratio`, `alphabetic_words`, `bullet_lines`, `ellipsis_lines`, `duplicate_lines`, `duplicate_line_chars`, `top_ngram_chars_2`, `top_ngram_chars_3`, `top_ngram_chars_4`, `duplicate_ngram_chars_5` to `duplicate_ngram_chars_10`, `stop_words`, `lorem_ipsum` |

## 8. Before a run: preflight and storage

`CorpusPreflight` runs the checks of `Corpus.Split` and reports instead of failing; it fetches no
shard. `CorpusPreflightData`:

| Field | Meaning |
|---|---|
| `WellFormed` | every line parsed, every source allowed and under the cap, the pack usable, the job inside the deployment's headroom |
| `TaskCount` | shards that parsed |
| `RefusedOrigins`, `OversizedSources` | hosts the list does not cover; sources over the cap |
| `EstimatedTrafficBytes` | what the contributors will fetch: every shard once, and the shards sampled for integrity a second time by their second machine (the largest taken as the sampled ones, as the storage estimate takes them); the sizes are the manifest's, the origin's answer to HEAD for the others (up to 64 sources, four at a time, 15 s each), the rest at the mean. The preflight's line names both parts |
| `EstimatedStorageBytes`, `StorageHeadroomBytes`, `StorageRetentionDays` | what the job leaves on the server, and the deployment's limit (below) |
| `BusiestNodeShare`, `PlannedNodes` | the busiest node's share of the work, 0 to 1, and how many nodes would take shards (below); 0 when the host has no pool to ask |
| `Messages` | every problem by manifest line, and the notes: the pack described, target languages it lacks, hosts that are not public origins, what the run keeps and whether `CorpusDedup` can run over it, MinHash parameters that are not the defaults, the storage line, the work split |

**The busiest share.** The contributor with the most work carries `BusiestNodeShare` of the run's
traffic. The server asks for the nodes that can take Corpus shards for the submitter now and plans
as the platform's allocator (`WitGridTaskAllocator` of the Grid controller, held to it by a test):
nodes by benchmark rate (a node that has not measured itself after every one that has, at the
slowest measured rate), the shards largest first, each to the node that finishes it earliest, the
two executions of a sampled shard on different nodes, then one node fewer while the longest share
grows by no more than 2 %. A shard's work is its size in MB. The platform picks the sampled shards
at random; the plan takes the largest. The answer is a share and a count: no node is named, and
the owner's rule is a percentage only.

**Storage.** Every execution of a shard uploads its text and signatures under `full`, and the
server keeps them for the retention window. The estimate is built from each shard's size and
ratios measured on real runs, the largest measured value raised by a fifth:

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

A document's signature is 8 bytes per hash (1,024 bytes at the default 128), plus a header of 12
bytes (16 with other parameters) per shard. The sampled shards count twice (the largest ones).
The report (4 KiB plus 3 KiB per shard), the integrity document (1 KiB plus 1 KiB per sampled
pair), the dedup document (2 KiB plus 512 bytes per shard plus 16 bytes per document) and the
inputs are added. Under **statistics and hashes only** there is no text, no signature, no second
copy and no dedup document: the estimate is the report, the integrity document and the inputs,
and the preflight line says the products stay on the nodes. On the test fleet twenty WET shards
(1,265 MiB fetched, sampling 0.25) left 770 MiB against an estimate of 983 MiB (1.28×).

**The limit.** The server's blob service may state a headroom per job (`BlobStorage:JobStorageHeadroomBytes`,
read by reflection) and the server process may set `OMNIBUSCLOUD_CORPUS_STORAGE_HEADROOM_BYTES`;
the smaller stated value holds. Over it, the preflight answers `WellFormed = false` naming both
numbers, and `Corpus.Split` fails the job before a node is asked. A deployment that states nothing
refuses nothing.

## 9. Determinism and integrity

What is guaranteed:

- **The product.** The same shard through the same pipeline and the same controller version gives
  the same bytes on Windows, Linux and macOS, on one thread or many: every stage is managed code
  with no platform-dependent path, or, for PDFs, a pinned WebAssembly module in a sandbox that
  counts instructions instead of time (fuel after instantiation, NaN canonicalisation, a pinned
  clock, seeded randomness).
- **The counters.** Records, documents, drops, languages, the unsupported breakdown and the PDF
  counts (fuel included) are functions of the shard and the pipeline.
- **The output mode** does not change the content hash or a counter.
- **The stage set.** `stages.sha256` is the hash of the benchmark's reference shard through the
  stages and of the PDF stage's contract, computed by the answering process; `stages.verified`
  says it is the pinned one. A controller release that changes a stage changes the product of an
  unchanged pipeline.

What is a metric, not identity: times, the bytes fetched (a resumed download is still the same
shard) and the degree.

Integrity:

- `SamplingRate` runs that share of the shards twice, in the same fan-out as the rest. The platform
  picks the shards at random and keeps the two executions on different machines, so a node cannot
  tell whether its shard is being checked.
- `corpus-integrity.json` (`corpus-integrity/4`) pairs the executions and compares what an honest
  node cannot vary: the verdict, the content hash, the signatures hash, the record and document
  counts, the drop histogram with the unsupported breakdown, the language histogram, the PDF
  counts (not the fuel: a cost, compared through the documents it produced). `differs` names the
  fields of a mismatch.
- Signatures that differ while the text agrees are a mismatch of their own kind: `signatures` in
  `differs`, the pair's `signatures` is `differ`, and `signature_mismatches` counts them (each is
  also a mismatch). A node returning the right text with wrong signatures would otherwise poison
  the near-duplicate merge unseen. A pair whose sides made no signature (`sign` off) has nothing of
  the kind to compare: its `signatures` is `none`, and `signatures_compared` counts only the pairs
  that had some.
- A pair whose sides ran different controller versions (a rollout in progress) is
  `version_skew`; a pair with a side that did not finish (`FetchFailed`, `PipelineUnavailable`,
  `Failed`) is `incomplete`. Neither is compared: a mismatch says two finished executions
  disagree. `mismatch_rate` is over the compared pairs.
- Under statistics and hashes only the pairs are compared exactly the same way: the signatures are
  made and hashed on the node, not kept.
- A side from a controller before 0.3.0 carries no signatures hash; its pair is version skew, never a
  mismatch.
- With one machine available nothing runs twice.

On the test fleet every sampled pair has agreed, Windows against Linux and macOS, the PDF
stage's included.

## 10. Scale, delivery and the nodes

- **Pattern U.** The task carries a URL; the node fetches the shard from the origin. Only
  descriptors, results and products travel through the platform.
- **The fetch.** Streamed to the task's own folder, capped (a `Content-Length` over the cap ends the
  request before a byte is read, a streamed body is cut), three attempts with back-off from 2 s and
  jitter on connection failures, 429 and 5xx, a read that stalls 60 s counts as a failure, a 4xx
  other than 429 is final, a broken body is resumed with a Range request. At most two shards
  download at once per node.
- **The language pack** arrives as a blob (cached per node) and is used only when its bytes hash to
  the pin. Blob transfers that fail are tried again for about five minutes.
- **Processing.** One streaming pass on a thread below normal priority; one shard per core at
  most, fewer when memory is short (256 MB budgeted per shard); a PDF's 1 GiB ceiling counts
  against half the process's memory. The task's folder is deleted when the task ends, whatever
  the verdict.
- **One plan, one send.** Each node gets a share by its benchmark rate (`corpus-wet-mb@synthetic-v2`:
  a synthetic reference shard of 400 documents per slot of the node's degree, its product's hash
  checked so a node that computes wrong cannot score; MB of real WET per second). A run ends when
  its slowest share ends; a node silent for 90 s loses its share, which is done again elsewhere.
- **Measured.** On a Ryzen 9 5950X, every stage on: the benchmark's calibration (2026-11-08, one
  thread) puts a MB of WET at 0.75 s of one core; the demo kit's 24 files took 26 to 33 s each
  (0.4 to 0.5 s per MB), eight at once, with Corpus 0.3.0 and the reference pack. A WARC file costs
  0.11 s per MB without its PDFs, 0.20 s with them. A six-node test fleet: twenty Common Crawl WET
  shards (1.25 GB) in 2 min 13 s, the traffic equal to the preflight's estimate; the pool rate 30.6
  MB of WET per second.

## 11. Limits and ceilings

| | Value |
|---|---|
| shard on the wire | 1 GiB (`MaxShardBytes` may lower it) |
| WARC record | 64 MiB (larger: skipped and counted) |
| HTML | the first 4 Mi characters are parsed; nesting 512 and 60,000 tags at most (more: `html_refused`) |
| redirects | 5, each hop put to the origin rule |
| size probes | 64 sources, 4 at a time, 15 s each |
| downloads per node | 2 at once |
| memory per shard | 256 MB budgeted |
| language identification | the first 4,000 characters; 20 letters at least |
| PDF | section 5 |
| MinHash | 1 to 1,024 hashes, 1 to 32 shingle words, bands dividing the hashes |
| dedup | 4,000,000 documents at 128 hashes (512 million signature values, about 4 GB; fewer documents with more hashes); 100,000 clusters listed |
| sampling | 0 to 1; one copy per sampled shard, on another machine |

## 12. What a lab sees and what never leaves the node

What a lab sees: the documents above. A result says which platform and degree ran a shard
(`linux-x64`, 16), never which machine: the result's node fields stay empty, the integrity
document names no machine, the capabilities count the pool, and the preflight's busiest share is
a share. Progress reaches the server per node, as counts.

What never leaves the node: the fetched shard and the task's folder (deleted when the task ends,
whatever the verdict; nothing is written outside the client's own folders); under statistics and
hashes only, the text and the signatures too.

**Where a machine fetches from** cannot be widened by a job. Every request passes two questions,
on a node and on the server alike: does the task name the host (`allowed_origins`), and does this
machine fetch from it at all. A machine fetches from the public origins compiled into the
controller (`data.commoncrawl.org`: over HTTPS, the default port, public internet addresses only;
the address checked is the address connected to), and from hosts its owner lists in
`OMNIBUSCLOUD_CORPUS_ORIGINS`, taken as written. Any other host answers `OriginRefused` without a
request. A URL with a user name or password is refused.

## 13. Demo kit

`AI/OutWit.Controller.AI.Corpus.Live/demo/01-common-crawl/` processes 24 public Common Crawl WET
files of CC-MAIN-2026-43 (1.58 GB on the wire) with the reference language pack and
`"output": "stats_and_hashes"`: the report and the integrity document, no text. Its `SCRIPT.md`
says what to run and what to look at, `check.py` turns the report into the funnel and the
language table and checks every shard's content hash against the kit's own list, and
`EXPECTED.md` has the numbers measured locally and the ones the fleet will add. The kit holds
hashes and counts only, no third-party text.

| | Measured locally (the fleet gives the same products) |
|---|---|
| Funnel | 515,379 records → 515,355 extracted → 453,528 after the language stage → 427,293 after the line stage → 273,891 out |
| Dropped | language 61,827 (58,953 unsupported, 2,874 too short), lines 26,235, quality 153,402 |
| Languages kept | 16; English 128,566, Russian 20,244, German 18,794, Spanish 17,019, French 16,052 lead |
| The product that was not kept | 448,706,065 bytes of compressed text (28.3 % of the bytes fetched) |
| Traffic at sampling 0.25 | 1,993,119,664 bytes: every file once (1,584,054,229) and the six largest a second time (409,065,435) |
| Server storage | 702,300 bytes at sampling 0.25 (1.15 GiB for the same run in full) |
| Identity | 24 content hashes, digest `c4b62530…f9eb50d9f4aa72`; 24 signatures hashes, digest `f661f7a9…cfd4fbb7` |

The same run in full, merged: 18,610 near-duplicates (6.8 %) in 8,858 clusters. A shard's own
near-duplicates, the most a node could report without a second pass, are 2,764 of them (15 %),
and a merge over the first 12 of the 24 shards finds 7,292: why no figure "so far" is offered.

## 14. Versions and what a deployment carries

| Part | Version | Where it comes from |
|---|---|---|
| The controller | 0.3.0 | `OutWit.Controller.AI.Corpus` (GitHub Packages; nuget.org at 1.0.0) |
| The wire types | 0.1.5 | `OutWit.Controller.AI.Corpus.Model`: the server restores it from the feed, so it is published before a controller that needs it |
| The scripts | 0.1.3 | `OutWit.Controller.AI.Corpus.Scripts` |
| The PDF extractor | pdftext 1.0.0 (pdf_oxide 0.3.78) | `corpus-pdftext-wasm.zip`, release `pdftext-v1.0.0` (5.9 MB, extracted to `wasm/` beside the controller) |
| The sandbox engine | wasmtime binding 48.0.2 over the native library 48.0.8 | natives per platform, release `wasmtime-v48.0.8` (8 to 10 MB each), the bytes Verify ships |
| The reference language pack | `cc-2026-43-16` | a job input; built by `corpus-assets` from Common Crawl WET files and the rule data in `OutWit.Controller.AI.Corpus.Assets/languages/` |

One wasmtime binding loads per client process: Corpus and Verify carry the same version.

## 15. Known limits

- The near-duplicate merge runs on the server after a run, over at most 4,000,000 documents; there
  is no figure "so far" during a run.
- A run waits for its slowest share (section 10).
- The language of a document is the language of its first 4,000 characters; a close relative the
  pack has no profile for (Galician, Belarusian, Afrikaans, Frisian, Luxembourgish) can still pass
  as the language it resembles.
- The line stage knows the sentence ends listed in section 5; in a script that ends sentences
  otherwise every line needs 12 words (40 letters) to stay.
- A PDF compound broken at its own hyphen (Cyrillic, Greek) comes out as one word.
- The storage table's WARC rows rest on one golden shard and one full file.
- The busiest share takes the largest shards as the sampled ones and every node as a machine of
  its own; the platform picks at random and knows machines.
- Shard-frequent lines (CCNet) and a URL blocklist are not in this release.
- The stages run inside the controller module only: an application cannot yet run them on its
  own machine to preview a sample (a package for that is designed, not built).
- The controller is proven on the test fleet. Its release to nuget.org and to the live network is
  the owner's decision.
