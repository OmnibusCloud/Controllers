# Corpus: what the controller can do

A reference for building on the Corpus controller. It covers:

- how to call it, and what goes in and what comes out;
- every stage, switch and threshold, every way a shard or a document can end;
- what is guaranteed about the results, and how a run is checked;
- what a lab sees, what never leaves a node, and where the edges are.

Every number on this page is a constant of the code or was measured, in the controller's tests
or on a test fleet; the [README](../README.md) explains how things work inside, and the JSON
schemas in this folder are the documents' contracts.

Version described: **Corpus 0.6.0** (Model 0.2.0, Scripts 0.1.5). Corpus 0.2.1 is proven on a
six-node test fleet (Windows, Linux, macOS); 0.3.0 added what an application's studio needs
(sections 4, 6 and 8); 0.4.0 was the first hardening release — what the server accepts from a
node, what a run does when nodes disagree or fail, what hostile input costs, and what a lab can
and cannot read off a document (sections 7, 9, 11 and 12) — and is proven on the test fleet;
0.5.0 is the second: what a run promises before it starts holds whoever wrote the manifest
(section 8), and the tasks and the results travel as blobs (section 10), and is proven on the
test fleet too; 0.6.0 is the third and moves no product: the module carries the licence notices
of what it redistributes (section 14), a line a node's log cannot take no longer decides a
shard's fate (section 12), and a shard that stops inside a header line is truncated, not
malformed (sections 5 and 7). What changed for an application is listed in section 14. 0.6.0 has not run on the build server or on the fleet yet. Nothing is on
nuget.org or on the live network yet.

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
| Input | Common Crawl WET and WARC shards (gzip), fetched by the node from `data.commoncrawl.org`, at most 1 GiB each and 1,048,576 a job |
| Stages | read → extract (HTML, and PDF in a wasm sandbox) → language identification → line cleaning → quality filter → compress → sign; the PDF, language, line, quality and signature stages each switchable |
| Languages | a language pack, pinned by hash: the reference pack supports 16 languages and knows 23 more, which it labels unknown instead of mislabelling them; a language is data, not code |
| Output | text and signatures per shard, or statistics and hashes only (`"output": "stats_and_hashes"`) |
| Determinism | the same shard through the same pipeline gives the same bytes on every node and platform |
| Integrity | a result is accepted only for the task it was issued for; a share of the shards runs twice on two different machines, copies that disagree are settled by three, and a shard nobody answered for is reported as such |
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
`Corpus.ProcessPackage`.

| Script | Inputs | Outputs | Use it for |
|---|---|---|---|
| `CorpusProcess` | manifest blob, language model blob, options | `report`, `integrity` | a run |
| `CorpusPreflight` | manifest blob, language model blob, options | `result` (`CorpusPreflightData`) | checking a manifest and stating the promises before a run (section 8) |
| `CorpusDedup` | report blob, options | `dedup` | the near-duplicate merge over a finished run |
| `CorpusRuntimeDiagnostics` | none | `result` (`CorpusRuntimeDiagnosticsData`), `capabilities` | what the deployment offers |

```
Job:CorpusProcess(Blob:manifest, Blob:languageModel, CorpusOptions:opts)
{
    CorpusTaskPackageCollection:tasks = Corpus.Split(manifest, languageModel, opts);
    Double:rate = Corpus.SamplingRate(opts);
    ProcessingOptions:checked = Grid.Replicas(2, rate);
    CorpusResultPackageCollection:executions = Grid.ForEach(package in tasks, checked)
        => Corpus.ProcessPackage(package);
    CorpusTaskPackageCollection:repairs = Corpus.Repairs(tasks, executions);
    CorpusResultPackageCollection:repaired = Grid.ForEach(package in repairs)
        => Corpus.ProcessPackage(package);
    CorpusTaskPackageCollection:disputes = Corpus.Disputes(tasks, executions, repairs, repaired);
    ProcessingOptions:majority = Grid.Replicas(3);
    CorpusResultPackageCollection:resolved = Grid.ForEach(package in disputes, majority)
        => Corpus.ProcessPackage(package);
    Blob:report = Corpus.Collect(tasks, executions, repairs, repaired, disputes, resolved, opts);
    Blob:integrity = Corpus.Integrity(tasks, executions, repairs, repaired, disputes, resolved);
}
```

Every collection of the script holds references to blobs: the tasks and the results travel in
packages (section 10). The second and third fan-outs run over nothing when a run goes well
(section 9).

Pass an empty blob (`Guid.Empty`) as `languageModel` when the pipeline's `identify_language` is
`false`.

### The activities

| Activity | Runs on | What it does |
|---|---|---|
| `Corpus.Split` | server | reads the manifest a source at a time, checks every source against the origin rule and the byte cap, pins the language pack's hash, refuses a pack the controller cannot use, asks the origin about a random sample of the shards (section 8), holds the storage estimate to the deployment's headroom, writes the tasks into task packages (blobs) and returns their references. Any problem fails the job here: a bad manifest never reaches a node |
| `Corpus.Preflight` | server | the same checks, reported instead of thrown (section 8) |
| `Corpus.ProcessPackage` | node | a task package start to finish: the checks a node makes before a batch (its version, its PDF sandbox, room on its disk), the package's blob held to its reference, then for every shard the origin re-check, the pinned pack, the fetch capped by the job's cap and the shard's declared size, the stages, the uploads (none under statistics and hashes only); the results go into one blob whose reference is returned. A shard's result is a verdict on the shard or a node fault (section 7) |
| `Corpus.SamplingRate` | server | the options' `SamplingRate` clamped to 0..1 |
| `Corpus.Repairs` | server | the shards the first round left without a result, as new tasks in new packages |
| `Corpus.Disputes` | server | the shards whose copies disagree, as new tasks in new packages for three machines |
| `Corpus.Collect` | server | the report, from the server's account of every round |
| `Corpus.Integrity` | server | the integrity document, from the same account |
| `Corpus.Dedup` | server | the near-duplicate merge |
| `Corpus.RuntimeDiagnostics` | server | the module's diagnostics |
| `Corpus.Capabilities` | server | the capabilities document |

### The options (`CorpusOptionsData`)

| Field | Default | Meaning |
|---|---|---|
| `MaxShardBytes` | 0 = the controller's cap, 1 GiB | the most bytes a shard may be on the wire; a smaller value lowers the cap, a larger one is held to 1 GiB |
| `SamplingRate` | 0 | the share of the shards run twice on another machine for integrity, 0 to 1 (rounded up to whole shards; in a job of more than 1,024 shards the share of its packages, every shard of a sampled package running twice: section 10) |
| `DedupThreshold` | 0 = 0.8 | the Jaccard estimate two documents need to be near-duplicates in `Corpus.Dedup` |
| `FetchConcurrency` | — | reserved; a node fetches at most two shards at once whatever it says |

### Before a run

`CorpusPreflight` answers without fetching a shard (section 8). `CorpusRuntimeDiagnostics` returns
the module's diagnostics and the `capabilities` document (schema
[`corpus-capabilities.schema.json`](corpus-capabilities.schema.json), `corpus-capabilities/3`):

| Field | What it holds |
|---|---|
| `controller`, `benchmark_unit` | the controller version; the unit of the nodes' benchmark rate |
| `stages` | the stages in order, the stage set's fingerprint and whether the answering process computes it, the language-pack format |
| `parsers` | the WARC reader, the HTML parser and the compressor with their versions |
| `language_model` | the formats read, the method, the reference pack's name, hash, supported and known languages |
| `formats`, `origins` | `wet`, `warc`; the public origins and the environment variable a machine's owner lists more hosts in |
| `defaults`, `ceilings` | section 11: the shard cap, the manifest's bytes and shards, the packages of a round and the shards of a package, the problems a preflight lists, the slack a declared size is given, the probes |
| `traffic` | the allowance of the traffic estimate (`retry_allowance`, 0.1) |
| `pipeline` | every switch with its default, `output` with its values, `minhash` with its defaults and ranges, every quality threshold for scripts with and without word spacing, the thresholds a manifest cannot set |
| `pdf` | the PDF stage: the module's hash, the engine, the limits, the running-line rule, the drop rules, whether the host can run it |
| `signatures` | the default MinHash parameters and the bytes of one document's signature |
| `drop_rules` | every drop rule with the stage that applies it, in the order a document meets them |
| `casing` | the Unicode version and the hash of the lower-case table the language and signature stages use (section 9) |
| `trust` | the copies a sampled shard runs in (2), the machines of a dispute (3) and its quorum (2); the outcomes `Disputed` and `NoResult` with the reasons of the latter; and every fixed word a document may carry in place of a text: `verdict_codes` per verdict, `fault_codes`, `rejection_reasons`, `dispute_outcomes`, `batch_refusals`, `dedup_exclusions` (sections 7 and 9) |
| `products` | each product's file, schema and the output modes that keep it |
| `storage` | the measured ratios of the storage estimate, what a task and a result cost in a package, the deployment's headroom and retention window when stated |
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
  on the node. The list narrows what a machine fetches from and cannot widen it (section 12). A
  host is written in ASCII: one written with other characters, in the list or in a source, is
  refused by name at the split (its ASCII form, `xn--…`, is the way to write it), because each
  machine's library would map it to its wire form and two machines need not map it alike.
- `sources`: a URL string, whose extension tells the format (`.warc.wet.gz` is WET, `.warc.gz`
  is WARC), or an object with `url`, `format` (`wet` or `warc`), `size` (bytes on the wire) and
  `sha256` (the expected hash: the node checks the fetched bytes and answers `FetchFailed` when
  they differ). A `size` is a claim the nodes enforce: the estimates and the work split count
  it, and a node refuses a shard far larger than it was declared (section 8). A URL listed
  twice is two tasks, with a note.
- A manifest is 256 MiB and 1,048,576 sources at most and is read a source at a time; one value
  of it is 4 MiB at most. The preflight lists its first hundred problems by line and counts the
  rest.

## 5. The stages

The stages in the order a document meets them (`stages.order` in the capabilities): `read`,
`extract`, `extract_pdf`, `identify_language`, `clean_lines`, `filter_quality`, `compress`,
`sign`. A drop is counted under the first rule a document fails; each rule belongs to one stage
(`drop_rules` in the capabilities).

**Read.** A gzip WARC or WET file (one member per record, as Common Crawl writes them) is
streamed one record at a time. A record over 8 MiB in a WET shard, or over 64 MiB in a WARC
shard, is skipped and counted (`record_oversized`); a truncated tail ends
the file with the complete records (the result's `error` is `truncated`, the verdict stays
`Completed`); bytes that are not a WARC file are `Malformed`. A tail is truncated wherever the
file stops: inside a payload, between two header lines, or inside a line. The last line of a
file that has no line feed is the line the cut went through and is not judged as a header; a
line that has its line feed and is malformed is `Malformed`, and so is a last line where a
record should start that is no beginning of `WARC/` (a body that is no WARC is not a truncated
WARC). Up to 0.5.0 a file that stopped inside a header line was `Malformed`: 63 of 5,000 evenly
spaced cuts of the head of a real WET file. A file that stops exactly between two records is a
shorter whole file, and nothing in it says so: packed, the gzip frame between two members and
the first bytes of the next unpack to nothing, which is 2 to 4 % of the places a cut can fall in
a real file. A shard may unpack to 32 times its
size on the wire (64 MiB at least, 16 GiB at most): past that it is `Oversized`
(`oversized_unpacked`) on every node, at the same byte.

**Extract.** A WET conversion record is a document as it is, its line structure kept. A WARC
response needs status 200 and an HTML body: the HTTP headers and body are parsed, the body decoded
by the header's charset, then the meta charset, then UTF-8 (a charset the runtime knows and will
not decode — UTF-7 — drops the page as `unsupported_charset`; a name nobody knows falls through
to the next source), and turned into text on AngleSharp
(scripts, styles, navigation, forms and hidden content dropped; blocks become lines; the title
first). A page is hostile input, and the parser's work on it is bounded twice. A linear
pre-scan reads the markup the way a tokenizer reads comments and text elements and refuses a page
nested deeper than 512 elements or with more than 60,000 tags (`html_refused`). The pre-scan is a
reading of its own, so the budget itself sits in the parser: the parse stops the moment an
element lands at depth 512 or the tree holds more than 120,000 elements, the ones the parser
makes without a tag included (`html_too_complex`). Both are functions of the page, the same on
every node. A page its decoder or the parser fails on in a way of their own — an error of logic
over its bytes, which the same bytes cause everywhere — drops as `html_error`: one page is never
the end of its shard. Only the first 16 MiB of a body are decoded, and a page longer than 4 Mi characters is
cut there before parsing. On the head of a real WARC file (112 of its pages) the extracted
text agrees with Common Crawl's own WET text at a median Jaccard of 0.85.

**PDFs (WARC only, `extract_pdf`).** A response with status 200 whose `Content-Type` or
`WARC-Identified-Payload-Type` is `application/pdf` and whose body starts with `%PDF-` is run
through `pdftext` (pdf_oxide 0.3.78 built for wasm32-wasip1, SHA-256 `13abba93…b5f6`) in the
controller's own wasmtime sandbox: it sees only its input file and an empty output folder, has no
network, a pinned clock and seeded randomness.

| Limit | Value |
|---|---|
| fuel | 8·10¹⁰ per PDF, counted from `_start` (given after instantiation, so it does not depend on the platform) |
| fuel per shard | 8·10¹² for all the PDFs of one shard together (a hundred full budgets; a Common Crawl WARC file's PDFs burn seven and a half): a PDF met after it is spent is not run and drops as `pdf_shard_fuel` |
| linear memory | 1 GiB |
| input | 64 MiB (a PDF Common Crawl truncated at 5 MiB is read all the same, and counted) |
| text | 16 MiB of UTF-8 |
| wall clock | 10 minutes, a backstop only: a run that reaches it is a fault of the node (`pdf_backstop`), never a verdict. A document that burns all its fuel takes 6.5 to 9 s of a desktop core, the same run after run |
| document | at most 5,000 words: pages are put together while they fit; a longer page is a document of its own, cut after its last whole line |

The text is shaped: pages split at form feeds, whitespace collapsed, running headers and footers
removed (a line that, with every run of digits folded, appears on half the pages or more of a PDF
of five pages or more), and Cyrillic or Greek words broken at a line end joined again. Each
page-range document is named `url#page=a-b` and goes through the stages below like a page of HTML.
Every limit, the module's pin and the shaping rules are part of the stage set's fingerprint. A
node that cannot run the sandbox makes no product of a WARC shard with the PDF stage on: it
refuses its batch before doing anything and the platform moves the work; a WET shard, and a WARC
shard with `extract_pdf` off, runs on it as on any other.

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
digits, lower-cased by the controller's own table: Unicode 16.0.0 simple lower case, section 9), `shingle_words` of them in a row, each shingle hashed
once (64-bit FNV-1a over UTF-8), and `hashes` universal hash functions (multiply-add-shift, seeds
from one constant through splitmix64) each keeping its minimum.

## 6. What comes back

| Product | Per | Kept under | Schema |
|---|---|---|---|
| `text.jsonl.zst` | shard execution | `full` | JSON lines, below |
| `signatures.bin` | shard execution | `full` | binary, below |
| `corpus-report.json` | run | both | [`corpus-report.schema.json`](corpus-report.schema.json), `corpus-report/4` |
| `corpus-integrity.json` | run | both | [`corpus-integrity.schema.json`](corpus-integrity.schema.json), `corpus-integrity/6` |
| `corpus-dedup.json` | dedup run | `full` | [`corpus-dedup.schema.json`](corpus-dedup.schema.json), `corpus-dedup/2` |
| `corpus-capabilities.json` | diagnostics run | — | [`corpus-capabilities.schema.json`](corpus-capabilities.schema.json), `corpus-capabilities/3` |

**Text.** zstd-compressed JSON lines, one per document that passed every stage,
`{"url","lang","text"}` in that order; `lang` is "" when the language stage is off; a PDF's
document carries `#page=a-b` in its `url`. The result's `content_sha256` is the SHA-256 of the
*uncompressed* lines: the identity a re-execution must reproduce. The server never opens a text
blob: whoever reads one should hash its uncompressed bytes and compare them with the report's
`content_sha256` first, as `Corpus.Dedup` does for the signatures — the hash is what the copies
were compared on, the blob is what one node uploaded.

**Signatures.** The magic `WITSIG1\n`, the hash count (int32 LE), then one signature per document
in product order (uint64 LE each): document *i* at `12 + i × 8 × hashes`. With a hash count or a
shingle size other than 128 and 5, the magic is `WITSIG2\n` and the shingle size (int32 LE)
follows the hash count: document *i* at `16 + i × 8 × hashes`. The SHA-256 of the blob's bytes,
kept or not, is the signatures' identity (`signatures_sha256`), which integrity compares.

**The report.** One entry for every shard of the job's task list, in task order, whatever came
back: `index` and `url` are the server's own, never a node's. Then `verdict` (the accepted
result's, or `Disputed`, or `NoResult` with a `reason`: section 7), `executions` (how many
agreeing executions the result rests on: 1 for a shard nobody else ran), `faults` (the codes of
the node faults met on the way, when there were any), `text_blob`, `signatures_blob`,
`content_sha256`, `error` (a fixed code or ""), `controller`, `runtime_target` (a platform such as
`linux-x64`, never a machine), `minhash` when signatures were made, `signatures_sha256`
("" when none was made), and `stats` (null for a shard without a completed result): `records_in`,
`documents_extracted`, `documents_out`, `bytes_fetched`, `bytes_produced` (the compressed product,
kept or not), `fetch_ms`, `process_ms`, `wait_ms`, `drops`, `languages`, `unsupported_languages`,
and the PDF counters where the shard met a PDF (`pdf_in`, `pdf_truncated`, `pdf_extracted`,
`pdf_pages`, `pdf_documents`, `pdf_pages_cut`, `pdf_running_lines`, `pdf_fuel`). The `summary`
sums them over the accepted results and adds:

- `verdicts`: shards per verdict, `Disputed` and `NoResult` among them;
- `single_execution`: the shards whose result is one machine's word; `rejected_results`,
  `repairs`, `disputes`, `disputes_resolved`: what did not go the plain way (section 9);
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

**The dedup document.** `CorpusDedup` reads the report, a shard at a time, and the signature
blobs it names. A
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
signatures were made with different MinHash parameters, a report that names one blob for two
shards, and a run past the capacity — asked of the report's document counts before a single blob
is read. Each blob is then held to what the report states for it: its length (the header and one
signature per document), its header and its SHA-256 (the `signatures_sha256` integrity
compared). A shard whose blob fails one is left out of the merge and listed in `excluded` with the
reason (`blob_missing`, `blob_length`, `blob_header`, `blob_hash`, `parameters`); the summary
counts them. On the test fleet the merge over twenty shards' signatures (203,462 documents) took
seconds. A figure "so far" during a run is not offered: the server holds no shard's signatures
before `Grid.ForEach` returns, and a shard's own near-duplicates are a small part of the run's
(section 13 has the numbers).

## 7. Verdicts and drop reasons

A shard ends in one verdict (the report's `verdict`). A verdict is a statement about the shard
that any honest node makes for the same bytes, so every verdict is compared across the copies of
a sampled shard; its `error` is a code of a fixed list (the capabilities' `trust.verdict_codes`),
never a text:

| Verdict | When | `error` |
|---|---|---|
| `Completed` | fetched and processed; under `full` its products uploaded | "" or `truncated` (the file ended inside a record, in its payload or in its headers: the records before it are the product) |
| `OriginRefused` | the URL is not one the task's list permits, or not one this machine fetches from; a redirect led off the list; nothing of the shard was read | `origin_not_allowed`, `origin_not_listed`, `origin_address`, `origin_redirect` |
| `FetchFailed` | the origin answered and its answer is not the shard: an HTTP status, bytes that do not hash to the manifest's `sha256`, a body without a length that ended inside a record | `http_NNN` (the status), `hash_mismatch`, `body_truncated` |
| `Oversized` | more bytes on the wire than the cap, more than the size the shard was declared with allows (section 8), or more unpacked than a shard may hold | `oversized`, `oversized_declared`, `oversized_unpacked` |
| `Malformed` | the bytes are not a WARC file, or the compression is damaged; a file that stops inside a record is not malformed (`truncated`, `body_truncated`) | `not_warc`, `bad_compression` |
| `PipelineUnavailable` | the pipeline identifies languages and names no model, or the model is not one the controller reads within its limits | `no_language_model`, `language_model_refused` |
| `Failed` | a task that is no task (no source, no pipeline, parameters no pipeline may have) | `task_invalid` |
| `Disputed` | the shard's copies disagreed and three machines did not settle it (section 9); no product, out of the counters | "" |
| `NoResult` | no machine returned a result that could be accepted, the repair round included; `reason` is `node_fault` (a machine that held the shard returned it as a fault of its own) or `no_answer` (nothing that was accepted came back: nothing at all, or only values that were dropped) | "" |

A node's own trouble is not a verdict. A node that cannot take a batch at all — another controller
version than the job's, no PDF sandbox for a WARC shard that needs one, no room on its disk —
refuses the batch before it fetches anything (`trust.batch_refusals`: `version`, `pdf_sandbox`,
`disk_space`, and `disk` or `internal` for a node that cannot make its work folder or fails while
it checks itself), and the platform gives the batch to another node. A fault met while working on a
shard returns that shard unanswered with a code (`trust.fault_codes`), and the node's other shards
are not lost with it:

| Fault | What happened on the node |
|---|---|
| `disk`, `disk_space` | a local file could not be written or read; less room than the shard needs |
| `network` | the origin did not answer this node after every retry (the node cannot tell its link from the origin) |
| `language_model` | the pack did not arrive, or arrived with other bytes than its pin |
| `pdf_sandbox`, `pdf_backstop` | the sandbox failed to run a document; a PDF reached the wall-clock backstop with fuel left |
| `upload` | a product could not be uploaded |
| `memory`, `internal` | out of memory; anything else inside the node |
| `tasks` | the node could not read its task package: the blob did not arrive, or is not the one its reference states. It answers every shard of the package |

A shard that came back as a fault is sent out once more; the report's `faults` lists the codes met
on the way, whatever the shard came to in the end.

A document that does not reach the product is counted under one rule:

| Stage | Rules |
|---|---|
| extract | `record_oversized` (a record larger than a record may be), `http_unparsable`, `http_status`, `not_html`, `unsupported_charset` (a charset the runtime will not decode), `html_refused` (the pre-scan's caps), `html_too_complex` (the parser's budget), `html_error` (the decoder or the parser failed on the page in a way of their own), `empty_text`, `pdf_encrypted` (a password is needed), `pdf_unreadable`, `pdf_no_pages`, `pdf_fuel` (out of fuel), `pdf_memory` (a refused allocation), `pdf_trap` (the module crashed), `pdf_error` (any other failure, or output that breaks the module's contract), `pdf_oversized` (more than 16 MiB of text), `pdf_empty` (text, but no line once shaped), `pdf_shard_fuel` (met after the shard's PDFs had spent their fuel together; not run) |
| language | `language_unknown` (fewer than 20 letters), `language_unsupported`, `language_not_target` |
| lines | `curly_bracket`, `no_prose_lines` |
| quality | `min_words`, `max_words`, `mean_word_length`, `symbol_to_word_ratio`, `alphabetic_words`, `bullet_lines`, `ellipsis_lines`, `duplicate_lines`, `duplicate_line_chars`, `top_ngram_chars_2`, `top_ngram_chars_3`, `top_ngram_chars_4`, `duplicate_ngram_chars_5` to `duplicate_ngram_chars_10`, `stop_words`, `lorem_ipsum` |

## 8. Before a run: preflight and storage

`CorpusPreflight` runs the checks of `Corpus.Split` and reports instead of failing; it fetches no
shard. `CorpusPreflightData`:

| Field | Meaning |
|---|---|
| `WellFormed` | every line parsed, every source allowed and under the cap, no declared size the origin contradicts, the pack usable, the job inside the deployment's headroom |
| `TaskCount` | shards that parsed |
| `RefusedOrigins`, `OversizedSources` | hosts the list does not cover (the first twenty); sources over the cap |
| `EstimatedTrafficBytes` | what the contributors' links will carry, down and up: the three fields below together, and the language model once for each node the plan uses when the server knows its pool. Up to 0.4.0 this was the fetches alone |
| `EstimatedFetchBytes` | every shard once, and the shards sampled for integrity a second time by their second machine (the largest taken as the sampled ones, as the storage estimate takes them) |
| `EstimatedUploadBytes` | the text and the signatures of every execution, by the storage table's ratios (none under statistics and hashes only), and the result packages |
| `RetryAllowanceBytes` | a tenth of the fetches: for a fetch that starts over, a shard a node returned as a fault of its own, a sampled shard whose copies disagreed. An allowance, not a bound |
| `FetchCeilingBytes` | what the nodes accept for those fetches at the most, whoever wrote the manifest: every shard at the size it is held to (below) |
| `UnsizedSources` | shards with no size of their own, the manifest's or the origin's: counted at the mean of the origin's answers, or at the cap, and held to the cap only |
| `EstimatedStorageBytes`, `StorageHeadroomBytes`, `StorageRetentionDays` | what the job leaves on the server, and the deployment's limit (below) |
| `BusiestNodeShare`, `PlannedNodes` | the busiest node's share of the work, 0 to 1, and how many nodes would take shards (below); 0 when the host has no pool to ask |
| `NodeDiskBytesPerShard` | the disk a node needs free for each shard it holds: the run's largest shard (the cap when no size is known), half of it again for the products, and 64 MiB. A node holds its degree and two more shards at a time, never its whole share; a node without room for a shard refuses its batch |
| `Messages` | the problems by manifest line (the first hundred, then how many more), and the notes: the pack described, target languages it lacks, hosts that are not public origins, what the run keeps and whether `CorpusDedup` can run over it, MinHash parameters that are not the defaults, how many shards a package holds in a job of more than 1,024, what the origin answered for the sample, the traffic line, the line on what the nodes enforce, the line on what the estimate leaves out, the node disk line, the storage line, the work split |

**A declared size binds.** A node holds a shard to the size it was declared with — the manifest's
`size`, or the origin's answer when the server asked — and an eighth more, 1 MiB at least: a
`Content-Length` past that ends the request before a byte is read, a streamed body is cut there,
and the shard's verdict is `Oversized` with the code `oversized_declared`. It is a verdict about
the input, the same on every honest node, never a fault of the node. A result that reports more
bytes fetched than its shard was held to is dropped on the server (`content`). A shard without a
size of its own is held to the cap only.

**The sample.** The server asks the origin (HEAD) for the size of up to 64 shards, four at a
time, 15 s each: half of them shards without a size and half shards with one (what one half
does not use goes to the other), drawn with the system's random generator, so a manifest cannot
arrange which are asked. An unsized shard that was asked takes the origin's answer as its size
and its bound; the others are counted at the mean of the answers, or at the cap when there is
none. A declared size the origin contradicts (the origin's is past the bound above) is a problem
by name — `sources[17]: the origin answers … bytes and the manifest declares …` — and stops the
job at the split. A sample finds a manifest that is wrong throughout; a single wrong size is
found by the node that fetches the shard.

**What the traffic figure is.** The estimate is what an honest manifest costs; the ceiling is
what the nodes enforce. Neither covers a node that fails or falls silent (its whole share is
fetched again by other nodes) or a run with many disputes and repairs (past the allowance; the
report's counters say what a run added): the preflight says both in a line of its own. The task
packages a node downloads (about a hundred bytes a shard) are not counted.

**The busiest share.** The contributor with the most work carries `BusiestNodeShare` of the run's
traffic. The server asks for the nodes that can take Corpus shards for the submitter now and plans
as the platform's allocator (`WitGridTaskAllocator` of the Grid controller, held to it by a test):
nodes by benchmark rate (a node that has not measured itself after every one that has, at the
slowest measured rate), the packages (a shard each in a job of up to 1,024 shards) largest first,
each to the node that finishes it earliest, the two executions of a sampled package on different
nodes, then one node fewer while the longest share grows by no more than 2 %. A package's work is
its shards' size in MB. The platform picks the sampled packages at random; the plan takes the
largest. The answer is a share and a count: no node is named, and
the owner's rule is a percentage only.

**Storage.** Every execution of a shard uploads its text and signatures under `full`, and the
server keeps them for the retention window. The estimate is built from each shard's size (a
shard nobody could size is counted at the cap a node holds it to, and the line says how many)
and ratios measured on real runs, the largest measured value raised by a fifth:

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

The rows with the filter on hold for the thresholds they were measured with: the controller's
defaults and the reference pack's rule data. A manifest that sets a `pipeline.quality` threshold
(the curly-bracket switch aside, which only drops more), or a language pack with rule data of its
own, is estimated at the row with the filter off, and the line says so. Measured on the head of
a real WET file: a manifest's ten thresholds opened wide keep 3.6 % more text than the defaults, a pack's
rule data opened too 22.5 % more, which is what a run with the filter off keeps.

A document's signature is 8 bytes per hash (1,024 bytes at the default 128), plus a header of 12
bytes (16 with other parameters) per shard. The sampled shards count twice (the largest ones).
The task packages (1,024 bytes and 128 a shard) and the result packages of every execution
(1,024 bytes and 768 a shard) are counted: they are blobs too.
The report (4 KiB plus 3 KiB per shard), the integrity document (1 KiB plus 1 KiB per sampled
pair), the dedup document (2 KiB plus 512 bytes per shard plus 16 bytes per document) and the
inputs are added. Under **statistics and hashes only** there is no text, no signature, no second
copy and no dedup document: the estimate is the report, the integrity document, the packages and
the inputs, and the preflight line says the products stay on the nodes. On the test fleet twenty WET shards
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
- **Case.** The language n-grams and the MinHash tokens are lower-cased by a table inside the
  controller (`casing` in the capabilities: Unicode 16.0.0, `UnicodeData.txt`'s simple lower-case
  mapping, U+0130 kept), not by the node's globalization library, which answers from ICU, from
  Windows NLS or from the runtime's own table depending on the machine (the three differ for
  hundreds of code points). ASCII names and phrases (HTML tags, header names, the line stage's
  phrases) are compared as ASCII. The table's hash is a part of the stage set's fingerprint.
- **One version per job.** The task carries the server's controller version: a node on another
  version refuses its batch, and a result that claims another version is dropped. Two copies of a
  shard are therefore always the same pipeline.
- **The counters.** Records, documents, drops, languages, the unsupported breakdown and the PDF
  counts (fuel included) are functions of the shard and the pipeline.
- **The output mode** does not change the content hash or a counter.
- **The stage set.** `stages.sha256` is the hash of the benchmark's reference shard through the
  stages, of the PDF stage's contract, of the case table and of the reading limits of section
  11, computed by the answering process; `stages.verified`
  says it is the pinned one. A controller release that changes a stage changes the product of an
  unchanged pipeline.

What is a metric, not identity: times and the bytes fetched (a resumed download is still the same
shard).

Integrity — what the server accepts from a node, and what a run does when nodes disagree or fail:

- **A result is bound to its task.** Every shard gets a random 128-bit ticket that travels in the
  task and comes back in the result, and every task package a ticket of its own that comes back
  with the answer; a later round gives new ones. An answer is bound to its package by the
  package's ticket before anything of it is opened. The server accepts a result only under a
  ticket of this job and round, in the answer to the package its shard was sent in, for that
  ticket's shard, from its own controller version, in the shape an honest node gives, and
  consistent with its task. Anything else is
  dropped and counted by reason (`rejected_results`; the integrity document's `rejected`, every
  reason written, 0 included): `unknown_ticket` (under no ticket of this job and round: an
  answer under no package ticket is not opened at all), `unreadable` (no result, or not shaped as
  an honest node shapes one; an answer whose blob is missing, damaged, larger than an answer may
  be or holds more results than its package had shards), `version` (another controller version
  than the server's), `content` (not what its task asked for: another shard's index or URL under
  this ticket, at odds with the task's pipeline, or more bytes fetched than the shard was held
  to), `surplus` (more values under one ticket than its round takes: an answer beyond the ones a
  round opens for a package, a second result for one shard in one answer; see *Two machines, not
  two values*). The words are the Verify controller's. Its `duplicate` (one results blob returned
  twice) has no counterpart in Corpus: two answers that name one blob are two answers, counted as
  any two under one ticket. A value that is dropped fails that value, never the run. A node
  knows only its own tickets, so it cannot answer for a shard it never had; a ticket is never
  written into a document.
- **Sampling.** `SamplingRate` runs that share of the shards twice, in the same fan-out as the
  rest. The platform picks the shards at random and keeps the two executions on different
  machines, so a node cannot tell whether its shard is being checked. A shard that is not sampled
  rests on one machine's word, and `single_execution` says how many do: on an unsampled shard a
  node can return anything an honest node could have returned for some shard. Raise the rate for
  what must be checked.
- **The comparison.** `corpus-integrity.json` (`corpus-integrity/6`) pairs the copies and compares
  what an honest node cannot vary: the verdict, the content hash, the signatures hash, the
  document counts, the drop histogram with the unsupported breakdown, the language histogram, the
  PDF counts (not the fuel: a cost, compared through the documents it produced). `differs` names
  the fields of a mismatch. Signatures that differ while the text agrees are a mismatch of their
  own kind (`signatures` in `differs`, `signature_mismatches`), and the near-duplicate merge holds
  each blob to the hash that was compared (section 6). A pair whose sides made no signature has
  `signatures: none`. A pair with a side that is no result (a node fault, a dropped value) is
  `incomplete`, not compared. `version_skew` is kept for readers of `/4` and is always 0: a node
  can no longer leave the comparison by the version it claims.
- **Disputes.** The server never chooses between two copies by what a node says (0.3.0 took the
  lowest content hash). Copies that disagree are run again on three machines; the result returned
  by the most of them, two at least and with no tie, is accepted, and otherwise the shard is
  `Disputed`: named in the report, out of the counters and the products. The integrity document's
  `disputes` lists what the three returned and how each dispute ended (`outcome`, beside
  `first_copies`, `copies` and `agreeing`): `resolved`; `no_majority` (no result was returned by
  more copies than any other, and by two at least); `too_few_copies` (fewer than two copies came
  back with a verdict); `surplus_copies` (below).
- **Repairs.** A shard a node returned as a fault of its own, or returned nothing acceptable for,
  is sent out once more; still without a result it is `NoResult` with its reason (`node_fault` or
  `no_answer`). The integrity document's `no_result` lists them with the fault codes met.
- **Two machines, not two values.** The server sees values, not machines: the platform keeps the
  copies of a package on different machines and hands over what they returned without saying
  which task each answer was for; the copies of a package carry one ticket, and so do the copies
  of each of its shards. Counting values as machines rests on two things: an answer gives a
  shard one value at most (a second result for one shard in one answer is dropped, `surplus`),
  and the platform — Grid 1.3.0 and later — keeps, of what a node's batch returns, one value at
  most for each return variable its tasks asked for. With an older Grid a node could return
  several answers for one task and this ruling would count them as machines. A machine can still
  spend a slot it holds on another ticket it holds, which leaves that one an answer short. Of the
  answers under a package's ticket a round opens one more than it asked for (three, two, four),
  so that one too many is seen; any further is counted as `surplus` and not read. Then:
  - in the first round more than two values under a shard's ticket send the shard to a dispute;
  - in the repair round (one machine, one value) more than one value under the ticket means none
    is taken: each is counted as `surplus` and the shard is `NoResult`;
  - in the dispute round more than three values under the ticket mean none is taken: each is
    counted as `surplus` and the dispute ends as `too_few_copies`. Otherwise the leading result
    must have been returned by two machines: of its values, as many as the other packages of the
    round are short of their three answers may be one machine's second, and do not count; when
    what is left is not two, or not more than the next result has, the dispute ends as
    `surplus_copies`.

  With one lying machine among the copies, whatever the others lose, a result accepted in a
  dispute was returned by an honest machine; three machines that agree still settle their shard
  when another package of the round lost a copy.
- **Cost.** A run that goes well runs no extra round. A disputed shard is fetched again by up to
  three machines, a repaired one once more; the traffic estimate's allowance (a tenth of the
  fetches) is for these, and the preflight says that it is not a bound.
- Under statistics and hashes only the copies are compared exactly the same way: the signatures
  are made and hashed on the node, not kept.
- With one machine available nothing runs twice. A dispute needs two machines that return the
  same: a pool of one never settles one, and a pool of two settles one only when both now agree
  and it is the only disputed package of the run (with two each is an answer short of its
  three, and two values under a ticket may then be one machine's: `surplus_copies`).
- **The limit.** A machine can spend a slot it holds on another ticket it holds. Such a second
  answer cannot win, but it can deny a result (four values end a dispute as `too_few_copies`; a
  round short of values elsewhere can end two against one as `surplus_copies`); and a machine
  that holds a sampled package's copy and a package of its own can spend the copy's slot on its
  own package, whose shards then show `executions: 2` that are one machine's. Closing both needs
  a ticket per copy from the platform.

On the test fleet every sampled pair has agreed, Windows against Linux and macOS, the PDF
stage's included.

## 10. Scale, delivery and the nodes

- **Pattern U.** The task carries a URL; the node fetches the shard from the origin. Only
  descriptors, results and products travel through the platform.
- **Tasks and results as blobs.** Nothing that grows with the manifest is an activity parameter or
  a return value. `Corpus.Split` writes the tasks into task packages (blobs of consecutive shards)
  and the script holds their references; a node is handed the references of its share, downloads
  each package, works through its shards and uploads one result package, returning its reference. A
  reference is under 200 bytes whatever the size of the job. A round has at most 1,024 packages: a
  job of up to 1,024 shards has one shard a package, a larger one `ceil(shards / 1024)` consecutive
  shards (98 for a hundred thousand), 1,024 at the most. The platform assigns, samples and re-runs
  packages: in a job of more than 1,024 shards `SamplingRate` is the share of the packages that run
  twice, and every shard of a sampled package runs twice. A package is an 8-byte header and a
  Brotli stream of bounded frames (the pipeline and the origins once, then 16 KiB a task at the
  most; 128 KiB a result); a task costs 93 bytes in it and a full result 568. A node holds a task
  package to the size and the hash of its reference before it fetches a shard; one it cannot read
  is answered with the node fault `tasks`. On the server the rounds are read one package at a time,
  the report and the integrity document are written a shard at a time, and the manifest is read a
  source at a time. Measured at 100,000 shards on the engine rig with six mock nodes: 1,021
  packages and 1,073 answers; at most 175 bytes handed to a node as a task's parameters, 65,453
  bytes returned as values in all (as values of their own: 53,493,864 and 189,172,800 bytes); the
  server read the whole run and wrote both documents in 16 MB of managed memory at its highest (the
  README has the rest).
- **The fetch.** Streamed to the task's own folder, capped by the task, by the node's own
  ceiling of 1 GiB and by the size the shard was declared with and an eighth more (a
  `Content-Length` over the cap ends the request before a byte is read, a streamed body is cut), three attempts with back-off from 2 s and jitter on connection failures
  and 5xx; an origin that says it is busy (429, 503) is waited for: up to six such answers, 5 to
  80 s apart or the origin's `Retry-After`, two minutes at most each. A read that stalls 60 s
  counts as a failure, and so does a fetch slower than 32 KiB/s over its whole length; a 4xx
  other than 429 is final; a broken body is resumed with a Range request; a body without a length
  that ends inside a record, in its payload or in its headers, is `FetchFailed`
  (`body_truncated`); one that ends exactly between two records cannot be told from a whole
  file (section 5). At most two shards download at once per node.
- **The disk.** A node holds its degree and two more shards at a time, whatever its share: a
  task waits for a place before it fetches. The preflight states the room each held shard needs.
- **The language pack** arrives as a blob (cached per node) and is used only when its bytes hash to
  the pin. Blob transfers that fail are tried again for about five minutes.
- **Processing.** One streaming pass on a thread below normal priority, the PDF extractor's
  guest code included; one shard per core at most, fewer when memory is short. A node's memory is
  budgeted once: half of it to shards (256 MB each), a quarter to PDFs (1 GiB each), the rest
  left to the machine. The shard and its products are deleted when the task ends, whatever the
  verdict, and the rest of the task's folder behind it.
- **One plan, one send.** Each node gets a share of the packages by its benchmark rate (`corpus-wet-mb@synthetic-v2`:
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
| shard on the wire | 1 GiB (`MaxShardBytes` may lower it); a shard with a declared size, that size and an eighth more, 1 MiB at least (more: `Oversized`, `oversized_declared`) |
| manifest | 256 MiB; 1,048,576 sources; 4 MiB a value; the first 100 problems listed (512 characters each), 20 hosts named |
| packages | 1,024 a round; 1,024 shards a package; a frame of 256 KiB for what the tasks share, 16 KiB a task, 128 KiB a result |
| record | 8 MiB in a WET shard, 64 MiB in a WARC shard (larger: skipped and counted, `record_oversized`) |
| unpacked shard | 32 times its size on the wire, 64 MiB at least, 16 GiB at most (more: `Oversized`, `oversized_unpacked`) |
| HTML | the first 16 MiB of a body are decoded and the first 4 Mi characters parsed; the pre-scan allows nesting 512 and 60,000 tags (more: `html_refused`); the parser's tree may not reach depth 512 or hold more than 120,000 elements (`html_too_complex`) |
| language pack | 16 MiB; 512 profiles of at most 16,384 n-grams, 524,288 n-grams together; 4,096 stop words per language; a node keeps the last four it used |
| redirects | 5, each hop put to the origin rule |
| size probes | 64 sources drawn at random (half without a size, half with one), 4 at a time, 15 s each |
| traffic allowance | a tenth of the fetches |
| downloads per node | 2 at once |
| shards on a node's disk | its degree and 2 more |
| memory per shard | 256 MB budgeted within half the machine's memory; a budget for the stages' working set, held by the record caps above (a WET record is at most 8 MiB) |
| language identification | the first 4,000 characters; 20 letters at least |
| PDF | section 5 |
| MinHash | 1 to 1,024 hashes, 1 to 32 shingle words, bands dividing the hashes |
| dedup | 4,000,000 documents at 128 hashes (512 million signature values, about 4 GB; fewer documents with more hashes), asked of the report before a blob is read; a report of 4 GiB at most (4,096 bytes for each shard a job may have), read a shard at a time; 100,000 clusters listed |
| sampling | 0 to 1; one copy per sampled shard, on another machine; three machines for a disputed shard |
| a result from a node | bounded strings, hashes of 64 hexadecimal digits, a code of the fixed list, counters that add up; anything else is dropped (`unreadable`) |
| an answer from a node | bound to a package by its ticket before it is opened; one more answer opened than the round asked for; 128 KiB a result at the most; one value a shard |

## 12. What a lab sees and what never leaves the node

What a lab sees: the documents above. A result says which platform ran a shard (`linux-x64`),
never which machine: the degree a node ran at is no longer written (it is the count of a
machine's cores, and a fleet with one 64-core machine would be told apart by it), the result's
node fields stay empty (a result that fills them, or the degree, is dropped), the integrity document names no machine, the capabilities count the pool, and
the preflight's busiest share is a share. Progress reaches the server per node, as counts.

No text written by a node reaches a lab. A result's `error` is a code of the fixed list in the
capabilities (`trust`); a node fault is a code; a batch refusal is one of five fixed sentences,
and no failure's own words are thrown out of a node's activity (what a node throws is the job's
message).
A message of a node's file system or HTTP stack — which names paths, accounts and hosts — stays
in the node's own log, and there as the kind of the failure and its system code. That log is the
node's and decides nothing: every line goes through one guard, one call at a time, and a host's
logger that throws (the in-memory logger of clients up to 2.2.5 does, when the packages of a
share log at once) loses the line and nothing else. Before 0.6.0 such a logger could fail a
package, and thrown inside the engine's own catch block it let the bare exception out in the
place of a refusal's fixed sentence. The diagnostics
say how many hosts the server's operator lists beside the public origins, not which. A test
scans every document of hostile and failed runs for a path, an account, a machine, a node id and
a ticket.

What never leaves the node: the fetched shard and the task's folder (deleted when the task ends,
whatever the verdict; nothing is written outside the client's own folders); under statistics and
hashes only, the text and the signatures too.

**Where a machine fetches from** cannot be widened by a job. Every request passes two questions,
on a node and on the server alike: does the task name the host (`allowed_origins`), and does this
machine fetch from it at all. A machine fetches from the public origins compiled into the
controller (`data.commoncrawl.org`: over HTTPS, the default port, public internet addresses only;
the address checked is the address connected to, on a machine that connects directly: behind a
proxy the proxy resolves the name), and from hosts its owner lists in
`OMNIBUSCLOUD_CORPUS_ORIGINS`, taken as written. Any other host answers `OriginRefused` without a
request. A URL with a user name or password is refused, and so is a host that is not written in
ASCII.

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
| Fetches at sampling 0.25 | 1,993,119,664 bytes: every file once (1,584,054,229) and the six largest a second time (409,065,435); the nodes accept 2,242,259,608 at the most |
| Traffic estimate at sampling 0.25 | 2,192,485,391 bytes: the fetches, uploads of 53,760 (the result packages: nothing else leaves a node) and an allowance of 199,311,967; the language pack (610,372 bytes) once for each node the plan uses is added when the server knows its pool |
| Server storage | 783,708 bytes at sampling 0.25 (1.15 GiB for the same run in full) |
| Identity | 24 content hashes, digest `c4b62530…f9eb50d9f4aa72`; 24 signatures hashes, digest `f661f7a9…cfd4fbb7` |

The same run in full, merged: 18,610 near-duplicates (6.8 %) in 8,858 clusters. A shard's own
near-duplicates, the most a node could report without a second pass, are 2,764 of them (15 %),
and a merge over the first 12 of the 24 shards finds 7,292: why no figure "so far" is offered.

## 14. Versions and what a deployment carries

| Part | Version | Where it comes from |
|---|---|---|
| The controller | 0.6.0 | `OutWit.Controller.AI.Corpus` (GitHub Packages; nuget.org at 1.0.0) |
| The wire types | 0.2.0 | `OutWit.Controller.AI.Corpus.Model`: the server restores it from the feed, so it is published before a controller that needs it |
| The scripts | 0.1.5 | `OutWit.Controller.AI.Corpus.Scripts` |
| The PDF extractor | pdftext 1.0.0 (pdf_oxide 0.3.78) | `corpus-pdftext-wasm.zip`, release `pdftext-v1.0.0` (5.9 MB, extracted to `wasm/` beside the controller) |
| The sandbox engine | wasmtime binding 48.0.2 over the native library 48.0.8 | natives per platform, release `wasmtime-v48.0.8` (8 to 10 MB each), the bytes Verify ships |
| The reference language pack | `cc-2026-43-16` | a job input; built by `corpus-assets` from Common Crawl WET files and the rule data in `OutWit.Controller.AI.Corpus.Assets/languages/`. That rule data holds FineWeb-2's stop-word lists and repetition thresholds (Apache-2.0): `LICENSE-fineweb-2.txt` is beside the pack wherever the repository hands it out as a file, and the module's notices name the pack |

One wasmtime binding loads per client process: Corpus and Verify carry the same version.

The module (the folder, the zip a node is given, the package's `content/module/`) carries, beside
the assemblies and `controller.json`, `THIRD-PARTY-NOTICES.txt` — AngleSharp (MIT), ZstdSharp
(MIT) and Zstandard, which it is a port of (BSD-3-Clause), the Wasmtime binding (Apache-2.0 WITH
LLVM-exception), FineWeb-2's stop-word lists (Apache-2.0) and the Unicode Character Database
(Unicode-3.0), each with its version, source commit and licence text — `LICENSE-fineweb-2.txt`,
`LICENSE-unicode.txt` and the controller's own `LICENSE.txt`: twelve files, held to that list by
a test. The two data assets carry their own notices (`LICENSE-wasmtime.txt` and `NOTICE.txt`
beside each native library, `wasm/LICENSE-pdftext.txt` beside the extractor).

**What changed for an application in 0.6.0** (from 0.5.0):

- **Nothing in a call or a document, and no product moved.** The scripts, the activities, the
  wire types (Model 0.2.0), the four schemas and every fixed word are those of 0.5.0, and no
  stage changed: what 0.4.0 and 0.5.0 made a product of, 0.6.0 makes the same product of, byte
  for byte, and the stage set's fingerprint is the same. A job still runs on one controller
  version, so server and nodes move to 0.6.0 together.
- **One verdict changed: a shard that stops inside a header line is truncated, not
  malformed.** A file whose last line has no line feed, where the cut went through the version
  line or a header of a record, ended as `Malformed` (`not_warc`) and gave no product. It now
  ends as every other cut file does: `Completed` with `error` `truncated` and the product of
  its whole records when the origin said where its body ends (a length, chunks),
  `FetchFailed` (`body_truncated`) when it did not. On a real WET file that is one cut in
  eighty. A report
  that counted such a shard under `Malformed` counts it under `Completed` or `FetchFailed`;
  no code is new. A line that has its line feed and is malformed, and a body that is no WARC at
  all, are `Malformed` as before (section 5).
- **The module is twelve files, not eight:** the four texts above travel in it. A deployment
  that lists or counts the files of a module, or pins the module's content digest, sees them.
- **A node's log cannot fail a package.** On a client that hands controllers a logger that is
  not thread safe (clients up to 2.2.5), a share of several packages could lose a batch to an
  exception of the logger's own, and the job's message could then be that exception's text
  instead of fixed words. Neither can happen now; the platform's reassignment of such a batch,
  which hid it, is no longer needed for it.
- **The scripts package** is unchanged (0.1.5). Its compatibility range for the controller, not
  yet switched on, now reads `[0.5.0, 2.0.0)`: it will admit the 1.0.0 the candidates become.

**What changed for an application in 0.5.0** (from 0.4.0):

- **`degree` left the report.** A shard's entry no longer has `degree` (`corpus-report/4`): the
  degree a node ran at is the count of a machine's cores, and the report names platforms, never
  machines. `runtime_target` stays. A reader that used `degree` has nothing to put in its place;
  one that ignored it changes nothing. A result that fills the field is dropped as `unreadable`.
- **The script's variables.** `CorpusProcess` still takes the manifest, the model and the options
  and still returns `report` and `integrity`. Its inner collections changed type and meaning:
  `tasks`, `repairs` and `disputes` are `CorpusTaskPackageCollection` (references to blobs of
  tasks), `executions`, `repaired` and `resolved` are `CorpusResultPackageCollection`, and the
  node activity is `Corpus.ProcessPackage` (`Corpus.ProcessShard` is gone). An application that
  only submits the bundled scripts changes nothing; one that wrote a script of its own over the
  old collections, or read `tasks` or `executions` from a finished job, must follow.
- **The traffic estimate means more.** `EstimatedTrafficBytes` was the fetches alone; it is now
  the fetches, the uploads, an allowance of a tenth of the fetches and, when the server knows its
  pool, the language model once per planned node. The fetches alone are in the new
  `EstimatedFetchBytes`; beside it `EstimatedUploadBytes`, `RetryAllowanceBytes`,
  `FetchCeilingBytes` and `UnsizedSources` (section 8). The preflight's traffic line reads
  otherwise and two more lines follow it.
- **A declared size is enforced.** A shard far above the `size` its manifest declares (an eighth
  more, 1 MiB at least) ends as `Oversized` with the new code `oversized_declared`, on every
  node; and a manifest whose sampled sizes the origin contradicts is not well-formed at the
  preflight and does not start at the split. Manifests with honest sizes see no change.
- **The storage estimate may be larger.** Shards nobody could size are counted at the cap (they
  were left out), a manifest that sets a quality threshold or a pack with rule data of its own is
  estimated as unfiltered, and the task and result packages are counted. A job that fitted a
  deployment's headroom by one of these may now be refused at the preflight.
- **Sampling in a large job.** Up to 1,024 shards nothing changes. Beyond, `SamplingRate` is the
  share of the job's packages, and every shard of a sampled package runs twice (section 10).
- **Bounds a lab may meet:** a manifest over 256 MiB or 1,048,576 sources, or with a value over
  4 MiB, is refused by name; a preflight lists the first hundred problems and counts the rest;
  a host that is not written in ASCII is refused by name (write its `xn--…` form); a report given
  to `CorpusDedup` may now be 4 GiB (it was 256 MiB) and is read a shard at a time.
- **New words:** the verdict code `oversized_declared`; the node fault `tasks` (a node that could
  not read its task package); `surplus` also counts an answer beyond the ones a round opens and a
  second result for one shard in one answer.
- **New schema versions:** `corpus-report/4` (no `degree`), `corpus-integrity/6` (the fault code
  `tasks`; the same shape), `corpus-capabilities/3` (new ceilings, `traffic`, the package
  constants in `storage`). `corpus-dedup/2` is unchanged, and `CorpusDedup` reads a report of
  `/3` as it reads one of `/4`.
- **The wire types** (Model 0.2.0): `CorpusTaskPackageData` and `CorpusResultPackageData` are
  new; `CorpusTaskData` gains `DeclaredBytes`, `CorpusPreflightData` the five fields above.
  `CorpusResultData.Degree` stays in the layout and is not filled.
- **Products** of an honest run on ordinary input are byte for byte those of 0.4.0: no stage
  changed, and the stage set's fingerprint is the same.

**What changed for an application in 0.4.0** (from 0.3.0):

- **The script's variables.** `CorpusProcess` still takes the manifest, the model and the options
  and still returns `report` and `integrity`. Inside it are two more fan-outs (`repaired`,
  `resolved`), empty in a run that goes well.
- **New outcomes in the report.** `verdict` may be `Disputed` or `NoResult` (with `reason`:
  `node_fault` or `no_answer`); such a shard has `stats: null` and no product. Every shard of the
  manifest is in the report, whatever came back.
- **New fields.** Per shard `executions` and, when there were any, `faults`; in the summary
  `single_execution`, `rejected_results`, `repairs`, `disputes`, `disputes_resolved`. In the
  preflight `NodeDiskBytesPerShard` and two more messages. In the diagnostics
  `MachineOriginCount` (the list `MachineOrigins` is no longer filled).
- **`error` is a code.** Free text is gone from every document; the codes per verdict are in the
  capabilities (`trust.verdict_codes`). `FetchFailed` now means the origin answered and did not
  give the shard; a node that could not reach the origin, write its disk or upload its product is
  a node fault and the shard is tried again, so `FetchFailed`, `PipelineUnavailable` and `Failed`
  are rarer and each says something about the input.
- **New drops:** `record_oversized`, `unsupported_charset`, `html_too_complex`, `html_error`,
  `pdf_shard_fuel` (all in the `extract` stage of the funnel).
- **New schema versions:** `corpus-report/3`, `corpus-integrity/5`, `corpus-dedup/2`,
  `corpus-capabilities/2`. The integrity document's pairs keep their shape and gain `kind` and
  `code` per side; `disputes` (each with its `outcome`), `no_result` and `rejected` are new. The
  dedup document gains `excluded`. The words of `rejected`, of a `NoResult` reason and of a
  dispute's `outcome` are the ones the Verify controller uses.
- **The platform it needs.** Grid 1.3.0 or later: the ruling counts values as machines, and that
  Grid keeps one value at most for each return variable a node's tasks asked for (section 9).
- **Refusals a lab may meet:** a language pack over 16 MiB or past the pack limits (at the
  preflight, by name); a WET record over 8 MiB is skipped (it was 64 MiB); `Corpus.Dedup` refuses a
  report that names one blob twice and leaves out a shard whose blob is not the one the report
  states.
- **Products** of an honest run on ordinary input are byte for byte those of 0.3.0, with one
  exception: a document that holds a capital letter added in Unicode 16 (27 code points: U+1C89,
  U+A7CB, U+A7CC, U+A7DA, U+A7DC and the Garay capitals U+10D50 to U+10D65) is signed and
  language-profiled with its lower case on every node, where 0.3.0 depended on the node's library.
  The stage set's fingerprint is new.

## 15. Known limits

- The near-duplicate merge runs on the server after a run, over at most 4,000,000 documents; there
  is no figure "so far" during a run.
- A shard that is not sampled is checked by nobody, and a machine can spend a slot it holds on
  another ticket it holds: it can deny a result, or make its own unsampled shard look checked
  (section 9).
- The pre-scan counts an element that is closed by the next tag without an end tag of its own
  (`<p>`, `<li>`, `<td>`, `<option>`) as nesting: a page with more than 512 of them in a row drops
  as `html_refused`, as in every release so far. The parser's own budget would let such a page
  through.
- While `Corpus.Split` or `Corpus.Preflight` runs, the server holds the job's plan: the parsed
  sources and their sizes, about 120 bytes a shard and two for every character of its URL (20 MB
  for a hundred thousand shards in the load run, some 40 MB with Common Crawl's URLs). The
  manifest's bounds (256 MiB, 1,048,576 sources) bound it; nothing else of a job is held per shard.
- In a job of more than 1,024 shards a package is the unit of everything the platform does: a
  sampled package runs all its shards twice, and a node that falls silent loses whole packages.
- The size probe is a sample: it finds a manifest whose sizes are wrong throughout, not one wrong
  size among thousands (the node that fetches that shard does).
- A run waits for its slowest share (section 10).
- The language of a document is the language of its first 4,000 characters; a close relative the
  pack has no profile for (Galician, Belarusian, Afrikaans, Frisian, Luxembourgish) can still pass
  as the language it resembles.
- The line stage knows the sentence ends listed in section 5; in a script that ends sentences
  otherwise every line needs 12 words (40 letters) to stay.
- A PDF compound broken at its own hyphen (Cyrillic, Greek) comes out as one word.
- The storage table's WARC rows rest on the head of one real WARC file and one full file.
- The busiest share takes the largest shards as the sampled ones and every node as a machine of
  its own; the platform picks at random and knows machines.
- Shard-frequent lines (CCNet) and a URL blocklist are not in this release.
- The stages run inside the controller module only: an application cannot yet run them on its
  own machine to preview a sample (a package for that is designed, not built).
- The controller is proven on the test fleet. Its release to nuget.org and to the live network is
  the owner's decision.
