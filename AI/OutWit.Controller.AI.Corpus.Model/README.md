# OutWit.Controller.AI.Corpus.Model

Data types for the OutWit Corpus controller: what a lab submits and what the
fleet returns when public web data (Common Crawl first) is turned into
training-grade text on anonymous nodes.

| Type | Role |
|---|---|
| `CorpusSourceData` | one fetchable shard: URL, format (WET or WARC), size estimate, published hash |
| `CorpusPipelineData` | the deterministic processing contract: version, stage switches (the PDF stage's `ExtractPdf` among them), `CorpusQualityData` thresholds, `CorpusMinHashData` (hashes, shingle words, LSH bands), what the run keeps on the server (`CorpusOutputMode`: everything, or statistics and hashes only), the language model pinned by hash and carried as a blob |
| `CorpusTaskData` | one node task: source + pipeline + the origins the node may fetch from + the byte cap, the task's `Ticket` (32 hexadecimal digits of 16 random bytes: the result must bring it back, and no document ever carries it) and `RequiredControllerVersion` (a node on another version refuses its batch) |
| `CorpusResultData` | what a node returns for a task: a verdict on the shard or a node fault (`CorpusShardVerdict`; `NodeFault` answers nothing and the shard is sent out again), the task's `Ticket`, a fixed code in `Error` (never a text), text and signatures blob ids, the content hash of the uncompressed product, `CorpusShardStatsData`, the controller version, the platform and degree it ran at (never the machine), the output mode, the MinHash parameters it signed with and the hash of its signatures (kept or not) |
| `CorpusShardStatsData` / `CorpusCountData` / `CorpusPdfStatsData` | records in, documents extracted and out, drops by rule, languages, bytes, times; what the PDF stage did with a WARC shard's PDFs |
| `CorpusOptionsData` | job-level knobs: shard byte cap, sampling rate, near-duplicate threshold |
| `CorpusPreflightData` | what a lab learns before anything runs: the manifest's health, the traffic estimate, the server storage estimate and the headroom it is held to, the busiest node's share of the work and how many nodes the plan uses (shares and counts, never a node), the disk a node needs free for each shard it holds |
| `CorpusRuntimeDiagnosticsData` | what the controller module of the answering process carries: version, stage set and its fingerprint, parser and model versions and hashes, the public origins and how many hosts the operator lists beside them (a count, never the names), the caps, the PDF extractor's pin and whether the process can run it |

Every type derives from `ModelBase` (value comparison with `Is`, deep `Clone`)
and is MemoryPack-serializable with explicit member order: new members are
appended, never inserted, so a deployed fleet keeps reading what a newer host
sends. Referenced by the controller package `OutWit.Controller.AI.Corpus` and by
anything that builds manifests or reads results without the controller.
