# OutWit.Controller.AI.Corpus.Model

Data types for the OutWit Corpus controller: what a lab submits and what the
fleet returns when public web data (Common Crawl first) is turned into
training-grade text on anonymous nodes.

| Type | Role |
|---|---|
| `CorpusSourceData` | one fetchable shard: URL, format (WET or WARC), size estimate, published hash |
| `CorpusPipelineData` | the deterministic processing contract: version, stage switches, `CorpusQualityData` thresholds, the language model pinned by hash and carried as a blob |
| `CorpusTaskData` | one node task: source + pipeline + the origins the node may fetch from + the byte cap |
| `CorpusResultData` | the product of a task: verdict (`CorpusShardVerdict`), text and signatures blob ids, the content hash of the uncompressed product, `CorpusShardStatsData`, the controller version, the platform and degree it ran at (never the machine) |
| `CorpusShardStatsData` / `CorpusCountData` | records in, documents extracted and out, drops by rule, languages, bytes, times |
| `CorpusOptionsData` | job-level knobs: shard byte cap, sampling rate |
| `CorpusPreflightData` | what a lab learns before anything runs: the manifest's health, the traffic estimate, the server storage estimate and the headroom it is held to |
| `CorpusRuntimeDiagnosticsData` | what the controller module of the answering process carries: version, stage set and its fingerprint, parser and model versions and hashes, the origin rule, the caps |

Every type derives from `ModelBase` (value comparison with `Is`, deep `Clone`)
and is MemoryPack-serializable with explicit member order: new members are
appended, never inserted, so a deployed fleet keeps reading what a newer host
sends. Referenced by the controller package `OutWit.Controller.AI.Corpus` and by
anything that builds manifests or reads results without the controller.
