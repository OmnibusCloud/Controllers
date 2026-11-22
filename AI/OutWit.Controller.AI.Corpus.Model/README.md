# OutWit.Controller.AI.Corpus.Model

Data types for the OutWit Corpus controller: what a lab submits and what the
fleet returns when public web data (Common Crawl first) is turned into
training-grade text on anonymous nodes.

This page describes **1.0.0**, the wire types of Corpus 1.0.0: the layout of 0.2.0, which Corpus
0.5.0 to 0.9.1 ran on, unchanged.

| Type | Role |
|---|---|
| `CorpusSourceData` | one fetchable shard: URL, format (WET or WARC), size, published hash |
| `CorpusPipelineData` | the deterministic processing contract: version, stage switches (the PDF stage's `ExtractPdf` among them), `CorpusQualityData` thresholds, `CorpusMinHashData` (hashes, shingle words, LSH bands), what the run keeps on the server (`CorpusOutputMode`: everything, or statistics and hashes only), the language model pinned by hash and carried as a blob |
| `CorpusTaskData` | one shard's task: source + pipeline + the origins the node may fetch from + the byte cap, `DeclaredBytes` (the size the shard was declared with, which the node holds it to; 0 when nobody declared one), the task's `Ticket` (32 hexadecimal digits of 16 random bytes: the result must bring it back, and no document ever carries it) and `RequiredControllerVersion` (a node on another version refuses its batch). Tasks travel inside task packages, never as parameters |
| `CorpusTaskPackageData` | the reference to a task package, which is what a node is handed: the blob that holds the tasks with its size and SHA-256, how many shards it holds and their bytes (the unit of the work split), the largest of them, how many have no size, whether one needs the PDF sandbox, the package's own `Ticket` and the controller version the job runs on |
| `CorpusResultPackageData` | what a node returns for a task package: the package's `Ticket` and the blob that holds its shards' results, or a fault code when the node could not work on the package at all |
| `CorpusResultData` | what a node says of one shard, inside a result package: a verdict on the shard or a node fault (`CorpusShardVerdict`; `NodeFault` answers nothing and the shard is sent out again), the task's `Ticket`, a fixed code in `Error` (never a text), text and signatures blob ids, the content hash of the uncompressed product, `CorpusShardStatsData`, the controller version, the platform it ran on (never the machine; `Degree` stays in the layout and is not filled: a result that fills it is dropped), the output mode, the MinHash parameters it signed with and the hash of its signatures (kept or not) |
| `CorpusShardStatsData` / `CorpusCountData` / `CorpusPdfStatsData` | records in, documents extracted and out, drops by rule, languages, bytes, times; what the PDF stage did with a WARC shard's PDFs |
| `CorpusOptionsData` | job-level knobs: shard byte cap, sampling rate, near-duplicate threshold |
| `CorpusPreflightData` | what a lab learns before anything runs: the manifest's health, the traffic estimate with its parts (fetches, uploads, an allowance) and the ceiling the nodes enforce, how many shards have no size, the server storage estimate and the headroom it is held to, the busiest node's share of the work and how many nodes the plan uses (shares and counts, never a node), the disk a node needs free for each shard it holds |
| `CorpusRuntimeDiagnosticsData` | what the controller module of the answering process carries: version, stage set and its fingerprint, parser and model versions and hashes, the public origins and how many hosts the operator lists beside them (a count, never the names), the caps, the PDF extractor's pin and whether the process can run it |

Every type derives from `ModelBase` (value comparison with `Is`, deep `Clone`)
and is MemoryPack-serializable with explicit member order: new members are
appended, never inserted or retyped, and the controller's tests hold every
member of every type to its order, name, type and default. What appending
gives, and what it does not:

- A value written by an older sender is read by a newer reader. The members the
  sender did not have arrive as the type's default (`0`, `false`, `null`), not
  as the property's initializer.
- A value written by a newer sender, with more members than the reader has, is
  refused by an older reader: MemoryPack checks the member count.

So the two sides of a job are one version: a job runs on one controller
version (a node on another refuses its batch before it reads a task), and this
package is published before a controller that needs it. Referenced by the
controller package `OutWit.Controller.AI.Corpus` and by anything that builds
manifests or reads results without the controller.
