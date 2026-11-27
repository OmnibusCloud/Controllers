# Smt in an application

This page is for the author of an application that runs SMT problems through the Smt
controller: a lab's own tool, a command line, an MCP server, a web page of results. It starts
from the job and goes in the order an application is built: submitting a job, what can be known
before a run, reading what a run gives, and what is promised. Each part is short and links to the
reference: [`capabilities.md`](https://github.com/OmnibusCloud/Controllers/blob/main/AI/OutWit.Controller.AI.Smt/docs/capabilities.md)
states the guarantees and the limits, the schemas beside it are the contract of the documents,
and the model package's [README](https://github.com/OmnibusCloud/Controllers/blob/main/AI/OutWit.Controller.AI.Smt.Model/README.md)
is the problem set, the reader, the sanitizer and the evaluator.

Written for **Smt 1.0.0** (with Smt.Model 1.0.0 and Smt.Scripts 1.0.1). The examples on this
page are written by the controller's own code: a test runs them and holds the page to what the
code writes.

## Contents

1. Submitting a job
2. Before a run
3. Reading the results
4. What is promised
5. The live tool, and what to take from the documents instead

## 1. Submitting a job

### The call

An application uploads the problem set as a blob, submits the script `SmtRun` with the blob's id
and the options, follows the job, reads the four variables that name the documents, and
downloads them. Through the platform's SDK (`OutWit.Cloud.SDK`):

<!-- example: sdk -->
```csharp
using OutWit.Cloud.Data.Processing;
using OutWit.Cloud.SDK;
using OutWit.Controller.AI.Smt.Model;

await using var client = new WitCloudClient(serverUrl, identityUrl, apiKey);   // a wit_sk_ application key
await client.ConnectAsync();

// The problem set goes up as a blob, in chunks; a break is resumed.
var problems = await client.Blobs.UploadBlobFromFileAsync("problems.jsonl");
var options = new SmtOptionsData { HardCapSeconds = 30, SamplingRate = 0.25 };

var job = await client.Scripts.SubmitAsync("SmtRun", [problems, options]);

// Follow the job until it ends; the progress is a number from 0 to 1.
var end = await job.WaitAsync<Guid?>("report", new Progress<double>(fraction => Console.WriteLine($"{fraction:P0}")), TimeSpan.FromSeconds(3));
if (end.Status != ProcessingJobStatus.Completed)
    throw new InvalidOperationException($"{end.Status}: {end.ErrorMessage}");

// The four documents are blobs, named by the job's variables.
foreach (var (variable, file) in new[] { ("results", "results.jsonl"), ("report", "report.json"), ("provenance", "provenance.json"), ("integrity", "integrity.json") })
{
    var blob = await client.Jobs.GetResultAsync<Guid?>(job.JobId, variable);
    await client.Blobs.DownloadBlobToFileAsync(blob!.Value, Path.Combine(folder, file));
}
```

The job's id (`job.JobId`) is all an application has to keep: the documents of a finished job are
read again by it, at any time while the platform keeps the job (see *Limits of a job*).
`SmtPreflight(Blob:problems, SmtOptions:opts)` is submitted the same way and gives its answer in
the variable `result`; `SmtRuntimeDiagnostics()` takes nothing and gives `result` and
`capabilities` (part 2).

### What the application references

- **`OutWit.Controller.AI.Smt.Model`, of the controller's version.** The options travel as an
  `SmtOptionsData` of this package, and the preflight and the diagnostics come back as its types.
  They are MemoryPack objects: a reader takes a payload of fewer members than it knows and throws
  on one of more, so the application's model package is the one the server's controller was
  built with (its provenance names it: `model`). The package is built for `net10.0` and depends on
  `OutWit.Common` 1.3.2 and `OutWit.Common.MemoryPack` 1.1.4. The same package reads and checks a
  problem set in the application with the server's own code (part 2).
- **`OutWit.Cloud.SDK`**, the platform's SDK, for the connection, the blobs and the jobs.

### The options

| `SmtOptionsData` | In the documents | Least | 0 or empty | Default | Greatest | Refused with |
|---|---|---|---|---|---|---|
| `Profile` | `profile` | | the controller's profile | `z3-5.4.1` | | `'profile' names a profile this controller does not have; it has 'z3-5.4.1'` |
| `Query` | `query` | | `Check` (the enum's 0) | `check` | | `'query' must be check, enumerate or prove` |
| `MaxModels` | `max_models` | 1 | the default | 2 | 1,000 | `'max_models' must be between 1 and 1000` |
| `Seed` | `seed` | 0 | a seed like the others, and the default | 0 | 2,147,483,647 | `'seed' must be between 0 and 2147483647` |
| `ResourceLimit` | `rlimit` | 1 | the profile's | 150,000,000 | 4,294,967,295 | `'rlimit' must be between 1 and 4294967295` |
| `HardCapSeconds` | `hard_cap_s` | 1 | the default | 60 | 600 | `'hard_cap_s' must be between 1 and 600` |
| `MemoryMegabytes` | `memory_mb` | 1 | the default | 1,024 | 4,096 | `'memory_mb' must be between 1 and 4096` |
| `SamplingRate` | `sampling_rate` | 0 | no package solved twice: a rate of its own | 0.05 | 1 | `'sampling_rate' must be between 0 and 1` |

`query` and `max_models` are what a row that names none takes; `rlimit` is the solver's own
counter of work for one check; `hard_cap_s` is seconds of a reference machine for one problem,
turned into fuel (a count, the same on every node); `memory_mb` is the most memory of one problem;
`sampling_rate` is the share of the packages solved on two machines (part 4). A new
`SmtOptionsData` holds the defaults, its sampling rate included. The documents state every option
as the number the run used, never the word "default", by the names of the second column.

The sentences of the last column are fixed: one an option, each starting with the option's name
between quotes, and a run's refusal joins them (`The run's options are refused: ` and the sentences,
separated by `; `, and a full stop). They carry no code: an application that maps a refusal to a
field maps it by the name between the first quotes. `SmtOptionsRules.Check` of the model package
gives the same sentences in the application before anything is sent.

### The request as a document

A host that is not .NET submits through the platform's job request document (the SDK's
`SubmitDocumentAsync`, the native SDK): the script by name and its parameters by kind. The problem
set is the blob's id, and the options a document of their type. This is the run of the call above:

<!-- example: request -->
```json
{
  "schemaVersion": 1,
  "script": "SmtRun",
  "parameters": [
    { "kind": "uuid", "value": "7b0c0f2d-5d3e-4c7a-9f1e-2a6b8c4d0e13" },
    { "kind": "document", "type": "smt.options@1", "value": { "hardCapSeconds": 30, "samplingRate": 0.25 } }
  ]
}
```

The model package publishes five types into the platform's document vocabulary:
`smt.options@1` (`SmtOptionsData`), `smt.preflight@1` (`SmtPreflightData`), `smt.diagnostics@1`
(`SmtRuntimeDiagnosticsData`), and the two the preflight is made of, `smt.count@1` (`SmtCountData`)
and `smt.refusal@1` (`SmtRefusalData`). A document names a member by the type's name for it in
camel case (`profile`, `query`, `maxModels`, `seed`, `resourceLimit`, `hardCapSeconds`,
`memoryMegabytes`, `samplingRate`), not by the documents' names of the option table; `query` is
`check`, `enumerate` or `prove`. A member left out is its default, as in a new `SmtOptionsData`
(the sampling rate's is 0.05), and `{ "kind": "null" }` in place of the document is the defaults
all. The server's door refuses a document with a member the options do not have (`hard_cap_s`, a
misspelt name) or a value of another type (`"30"` for a number) as a request of the wrong shape,
and no job starts; a number out of its range passes the door, and the job fails with the
sentence of the option table (*How a job ends*).

The documents' variables (`results`, `report`, `provenance`, `integrity`, `ledger`, and
`capabilities` of the diagnostics) are blob ids and read as `uuid` values. The `result` of
`SmtPreflight` and of `SmtRuntimeDiagnostics` reads as a value document of `smt.preflight@1` and of
`smt.diagnostics@1` (the SDK's `GetVariableDocumentAsync`), its members named as above. The model
package carries the vocabulary for a host without .NET under `documents/`: `smt.schema.json`, a
JSON Schema of the five types, and `smt_documents.py` and `smt_documents.hpp`, bindings for Python
and C++ that write a request's document and read a value document.

### Limits of a job

| | |
|---|---|
| Rows of a set | 1,000,000; the row after them is refused once (`too_many_problems`), and the run goes on with the rows before it |
| Bytes of a set | 4 GiB (4,294,967,296 bytes), every byte counted, blank lines and line ends among them; the line that would pass them is refused once (`set_too_large`) |
| A line of a set | 16 MiB (16,777,216 bytes), its line end not counted (`row_too_long`) |
| A problem | 4,194,304 characters, 2,000,000 tokens, nesting 100,000 (the model package's ceilings) |
| What a lab sees of a row's models | 65,536 bytes of UTF-8 a row; past them the texts are null and `model_truncated` is true |
| A deadline | none of the controller's: every problem has its hard cap, as fuel, and its session a wall-clock backstop that is the node's, never a verdict |
| The documents | blobs of the server's store, named by the job's variables; read again by the job's id for as long as the platform keeps the job and its blobs. The controller sets no retention of its own and states none |

**Progress.** A job's progress (`OverallProgress`, 0 to 1) is the platform's number. It moves with
the statements of the script (the activities done of the script's) and, inside each of the three
fan-outs, with what the nodes report of their share (the packages received, then the problems
done); it is 1 when the job completes. On a run where every node answered, the repair round and
the dispute round send nothing and pass at once. The server's own work, the split before the
first fan-out and the settlement after the last (every `sat` model through the evaluator), reports
no progress of its own: on a large set the number stands still there for minutes. It is no
estimate of the time left.

### How a job ends

A run that cannot start, or cannot be settled, fails as a job; the job's error carries the
controller's sentence:

| When | The job's error carries |
|---|---|
| the set has no row | `The problem set has no row.` |
| an option is out of range | `The run's options are refused: 'hard_cap_s' must be between 1 and 600.` (every option out of range, as above) |
| the profile is not the controller's | `The run's options are refused: 'profile' names a profile this controller does not have; it has 'z3-5.4.1'.` |
| the nodes' rows, or the full models of the settled rows, pass what the server keeps of one run | the bound's name: `smt_rows_bound` (128 GiB of rows) or `smt_models_bound` (2 GiB of full models) |

A set whose every row is refused is no failure: the run sends nothing to a node and its results
say why for each row. A problem that fails on its own (a refusal, an error of the solver's, a crash,
a budget) is a row of the results with its status, never a failed job. Which nodes take a package
is the platform's planner's: it sends one only to a node that reports 7,680 MiB of memory or more
(`node.min_ram_mb` of the capabilities); what the platform does with a job that no node can take
is the platform's, and the capabilities' `pool` says beforehand how many nodes the planner would
send a package to.

**Cancel.** A cancelled job has no documents, unless the cancel came after they were written: the
settlement, which writes them, stops at its next row and writes nothing, as every activity of the
server's stops at the next package, row or model it reads, its working files going with it; nodes
hand out no more problems. The packages the run gave out and what the fan-outs that ended returned
stay in the job's variables, as the platform keeps a job's variables (the README's *What the job
keeps*).

## 2. Before a run

### What an application knows without the server

The model package reads a set with the server's own code, before anybody signs in:
`SmtProblemSetReader` gives every row taken or refused (with the code and a reason), and counts the
taken problems by logic, by query and by expected verdict, the commands the sanitizer leaves out
and the licences; `SmtOptionsRules.Check` gives the sentences above, and `Resolve` the options with
their defaults filled in; `SmtSanitizer` gives a problem's canonical text and its hash. The
SHA-256 of the set is the SHA-256 of its bytes. The capabilities document (below) gives the rest a
form needs: the defaults and the ceilings, the fuel of a second, the memory of a session under the
default options, the least memory of a node, the pool.

What only the server says is the controller's own word on a set: the packages a run would cut it
into, the memory a node needs for one session under the given options, and whether the profile is
the controller's. That is the preflight, and it costs the upload of the set.

### The preflight

`SmtPreflight` reads the set as a run reads it, sends nothing to a node, and gives an
`SmtPreflightData`:

| Field | |
|---|---|
| `Runnable` | whether a run would start: the options in range, the profile the controller's, a row in the set. A set whose every row is refused is runnable |
| `Rows`, `Accepted`, `Refused` | the rows (blank lines are none), taken and refused |
| `BytesRead`, `Sha256` | the bytes of the set read, and their SHA-256 (the provenance's `problem_set.sha256`) |
| `RefusalCodes` | the refused rows by code, the codes in ordinal order |
| `Refusals` | the first 100 refusals in full (below) |
| `Logics`, `OtherLogics` | the taken problems by the logic their `set-logic` names (empty for none), 64 logics at most, and the problems of the others |
| `Queries`, `Expected` | the taken problems by query, and by expected verdict (`sat`, `unsat`, `none`) |
| `Stripped` | the commands the sanitizer left out of the taken problems, by name |
| `Licenses`, `OtherLicenses` | the taken problems by the licence their `:license` names (empty for none), 64 at most, and the problems of the others |
| `OptionProblems` | what is wrong with the options: the sentences of the option table, and the profile's; the set is still read, each such option at its default |
| `Profile` | the profile a run would use |
| `ResourceLimit`, `HardCapSeconds`, `Fuel`, `MemoryMegabytes`, `MaxModels`, `Seed`, `SamplingRate` | the budget of a problem as numbers (`Fuel` is the hard cap as the count the sandbox stops at) |
| `SessionMemoryBytes` | the memory a node needs available for one session under the options: a node with less refuses the package (`memory_insufficient`) |
| `UpperBoundSeconds` | the taken problems times the hard cap, in seconds of the reference machine: a bound, not an estimate |
| `Packages`, `ProblemsPerPackage` | the packages a run would cut the taken problems into, before any copy, and the most problems of one |
| `Messages` | fixed sentences (below) |

A refusal (`SmtRefusalData`) names the row by `Row`, the line of the set from 1, blank lines
counted; `File` is the row's id (empty when the row has no readable id); `Line` is the line inside
the problem's or the term's text the sanitizer refused (0 for a refusal of the row itself);
`Command` the command's name or nothing; `Code` a fixed word; `Reason` a sentence that never
carries the problem's text. The messages are these sentences and no other:

- `The problem set has no row: a run would be refused.`
- `Every row of the problem set is refused: a run would send nothing to a node, and its results would say why for each row.`
- `The upper bound is the taken problems times the hard cap, in seconds of the reference machine: a bound, not an estimate. How long an SMT problem takes cannot be told from its text.` (always)

The preflight of the example set of part 3, under the example's options, printed here with the
members' names of the type (as a value document of `smt.preflight@1` the same members are named in
camel case):

<details><summary>The preflight of the example set</summary>

<!-- example: preflight -->
```json
{
  "Runnable": true,
  "Rows": 5,
  "Accepted": 4,
  "Refused": 1,
  "BytesRead": 598,
  "Sha256": "e23af6fc683f87cf7b873646c56f6040d3b2a2ab4e106bef6c1ef63a7908336f",
  "RefusalCodes": [
    {"Name": "command_not_allowed", "Count": 1}
  ],
  "Refusals": [
    {
      "File": "push",
      "Line": 1,
      "Command": "push",
      "Code": "command_not_allowed",
      "Reason": "incremental scripts, unsat cores and proofs are not supported",
      "Row": 6
    }
  ],
  "Logics": [
    {"Name": "QF_LIA", "Count": 4}
  ],
  "OtherLogics": 0,
  "Queries": [
    {"Name": "check", "Count": 2},
    {"Name": "enumerate", "Count": 1},
    {"Name": "prove", "Count": 1}
  ],
  "Expected": [
    {"Name": "none", "Count": 2},
    {"Name": "sat", "Count": 1},
    {"Name": "unsat", "Count": 1}
  ],
  "Stripped": [],
  "Licenses": [
    {"Name": "", "Count": 4}
  ],
  "OtherLicenses": 0,
  "OptionProblems": [],
  "Profile": "z3-5.4.1",
  "ResourceLimit": 150000000,
  "HardCapSeconds": 60,
  "Fuel": 300000000000,
  "MemoryMegabytes": 1024,
  "MaxModels": 2,
  "Seed": 0,
  "SamplingRate": 1,
  "SessionMemoryBytes": 2885681152,
  "UpperBoundSeconds": 240,
  "Packages": 1,
  "ProblemsPerPackage": 16,
  "Messages": ["The upper bound is the taken problems times the hard cap, in seconds of the reference machine: a bound, not an estimate. How long an SMT problem takes cannot be told from its text."]
}
```

</details>

### The diagnostics

`SmtRuntimeDiagnostics` says what the controller of the server's process carries and can do,
in an `SmtRuntimeDiagnosticsData`. It describes the server's process, not the nodes, and names
no machine:

| Field | |
|---|---|
| `ControllerVersion`, `ProfileId`, `SolverVersion` | the controller and its one solver profile |
| `ModuleFile`, `ModuleSha256`, `ModuleSize` | where the solver module is looked for, relative to the controller's folder, and its pins |
| `ModulePresent`, `ModuleCompiled`, `CompileMs` | whether this process has the module with the pinned bytes, whether it has compiled it, and how long the compile took (0 before) |
| `SandboxProblem` | why this process has no sandbox, in fixed words without a path; empty when it has one or has not tried |
| `WasmtimeBinding`, `NativesVersion`, `NativesSha256`, `NativesPinned` | the engine's binding loaded, the native library the controller ships, its SHA-256 for this platform, and whether the library loaded is the pinned one |
| `TrapMode` | `explicit` or `signals`: how the solver's traps are taken |
| `Platform` | `win-x64`, `linux-x64` or `osx-arm64` |
| `LogicalCores`, `AvailableMemoryBytes`, `SessionMemoryBytes`, `Degree`, `DegreeReason` | the sessions a package would get in this process under the default options, and what decided it: `cores`, `cap`, `memory`, or `no_memory` |

Asking compiles nothing. Which Grid the server has is not among them (part 4).

### The capabilities document

`SmtRuntimeDiagnostics` gives beside it the capabilities document (`capabilities`, a JSON blob,
[`smt-capabilities.schema.json`](https://github.com/OmnibusCloud/Controllers/blob/main/AI/OutWit.Controller.AI.Smt/docs/smt-capabilities.schema.json)):
the profile, the queries, the defaults and the ceilings of the options and of a problem, the words
of every document of a run, the size of a package, the degree rule and the least memory of a node,
the benchmark's unit, the platforms, and, when the server can say, the pool as totals (`nodes`,
`measured`, `workers`, `rate`). Every list of words is the list the controller writes the documents
with: a legend or a filter built from it meets no word it does not list. The document of this
version, without a pool:

<details><summary>The capabilities document</summary>

<!-- example: capabilities -->
```json
{
  "schema": "smt-capabilities/1",
  "controller": "1.0.0",
  "profile": {
    "id": "z3-5.4.1",
    "solver": "z3",
    "solver_version": "5.4.1",
    "module": "wasm/z3-5.4.1.wasm",
    "module_sha256": "9b8a67e85542b5d62d30b40060dd237fcef07f794ea235483afc83eef47a1364",
    "module_size": 30204160,
    "module_present": true,
    "engine": "wasmtime 48.0.8"
  },
  "queries": ["check", "enumerate", "prove"],
  "defaults": {
    "max_models": 2,
    "seed": 0,
    "rlimit": 150000000,
    "hard_cap_s": 60,
    "memory_mb": 1024,
    "sampling_rate": 0.05
  },
  "ceilings": {
    "max_models": 1000,
    "seed": 2147483647,
    "rlimit": 4294967295,
    "hard_cap_s": 600,
    "memory_mb": 4096,
    "sampling_rate": 1
  },
  "time": {"fuel_per_second": 5000000000, "backstop_base_ms": 60000, "backstop_slowdown": 10},
  "session": {
    "output_bytes": 16777216,
    "transcript_bytes": 8192,
    "message_characters": 1024,
    "memory_headroom_bytes": 16777216
  },
  "problem": {
    "max_characters": 4194304,
    "max_tokens": 2000000,
    "max_depth": 100000,
    "max_numeral_digits": 4096,
    "max_literal_bits": 65536,
    "max_string_characters": 65536,
    "max_declared_symbols": 100000,
    "max_index": 65536,
    "max_term_characters": 65536
  },
  "problem_set": {
    "max_line_bytes": 16777216,
    "max_rows": 1000000,
    "max_id_characters": 64,
    "max_values": 256,
    "max_project": 256,
    "max_tags_characters": 256
  },
  "results": {
    "statuses": ["ok", "refused", "input_error", "output_exceeded", "engine_crash", "no_result", "disputed"],
    "codes": {
      "refused": ["ambiguous_token", "builtin_name", "command_after_end", "command_malformed", "command_not_allowed", "field_invalid", "field_missing", "field_not_allowed", "id_duplicate", "id_invalid", "index_too_large", "literal_too_wide", "nesting_too_deep", "non_ascii_string", "not_a_command", "not_a_term", "numeral_too_long", "reserved_prefix", "reserved_symbol", "row_malformed", "row_too_long", "set_too_large", "string_too_long", "syntax_error", "text_too_long", "too_many_problems", "too_many_symbols", "too_many_tokens", "unknown_constant"],
      "engine_crash": ["trap", "exit", "engine_error", "unreadable_answer"],
      "no_result": ["node_refused", "sandbox_unavailable", "backstop", "cancelled", "not_instantiated", "host_fault", "version_mismatch", "package_invalid", "ticket_invalid", "package_too_large", "package_hash_mismatch", "package_unreadable", "package_mismatch", "memory_insufficient", "download_failed", "upload_failed", "unknown_ticket", "duplicate", "unreadable", "content", "surplus", "other_package", "no_answer"],
      "disputed": ["no_majority", "too_few_copies", "surplus_copies"]
    },
    "verdicts": ["sat", "unsat", "unknown", "none"],
    "reasons": ["rlimit", "fuel", "memout", "incomplete", "other", "none"],
    "proofs": ["proved", "counterexample", "none"],
    "expected": ["sat", "unsat", "none"],
    "expected_agreements": ["agree", "disagree", "n/a"],
    "validations": ["validated", "partial", "failed", "none"],
    "validation_reasons": ["assertion_false", "value_mismatch", "completed_symbol", "quantifier", "algebraic_number", "division_by_zero", "regular_expression", "floating_point", "selector_of_other_constructor", "unsupported_operator", "unsupported_sort", "array_comparison", "string_literal", "ill_formed_term", "unknown_symbol", "ill_formed_command", "ambiguous_symbol", "unreadable_model", "unreadable_values", "budget_steps", "budget_depth", "budget_size", "evaluator_error", "output_exceeded", "budget_package"],
    "settled_by": ["one_copy", "copies", "dispute", "agreement"],
    "node_faults": ["version_mismatch", "package_invalid", "ticket_invalid", "package_too_large", "package_hash_mismatch", "package_unreadable", "package_mismatch", "sandbox_unavailable", "memory_insufficient", "download_failed", "upload_failed"]
  },
  "run": {
    "sampling": ["not_stated", "none", "as_asked", "short"],
    "bindings": ["bound", "unbound", "mixed", "none"],
    "rounds": ["main", "repair", "dispute"],
    "repair_reasons": ["node_refused", "sandbox_unavailable", "backstop", "cancelled", "not_instantiated", "host_fault", "version_mismatch", "package_invalid", "ticket_invalid", "package_too_large", "package_hash_mismatch", "package_unreadable", "package_mismatch", "memory_insufficient", "download_failed", "upload_failed", "unknown_ticket", "duplicate", "unreadable", "content", "surplus", "other_package", "no_answer", "copies_short"],
    "rejected": ["unknown_ticket", "duplicate", "unreadable", "content", "surplus", "other_package"],
    "dispute_causes": ["digests_differ", "model_failed", "surplus"],
    "dispute_outcomes": ["resolved", "no_majority", "too_few_copies", "surplus_copies"],
    "stopped_by": ["rlimit", "fuel", "memout"]
  },
  "packages": {
    "max_problems": 256,
    "max_bytes": 269484032,
    "max_problem_frame_bytes": 134217728,
    "max_result_frame_bytes": 50397184
  },
  "node": {
    "min_ram_mb": 7680,
    "max_degree": 16,
    "ram_headroom": 0.75,
    "session_margin_bytes": 201326592,
    "session_bytes": 2885681152,
    "priority": "below_normal"
  },
  "benchmark_unit": "smt-problem@ref-v1",
  "platforms": ["win-x64", "linux-x64", "osx-arm64"]
}
```

</details>

### Reading a model in the application

`SmtModel.Read(model, values, limits)` of the model package reads a model as the solver printed
it. A text of a row of the results is bounded already (65,536 bytes), so
`SmtReaderLimits.Unbounded` reads it; a text from anywhere else is read under limits of the
application's own. The server reads the models a node returns under its own limits (32 Mi
characters, 8,388,608 tokens: the README's *Settling*).

## 3. Reading the results

### The documents

| Variable | Document | Schema |
|---|---|---|
| `results` | one JSON line a row of the set, in the set's order, refused rows included | [`smt-results.schema.json`](https://github.com/OmnibusCloud/Controllers/blob/main/AI/OutWit.Controller.AI.Smt/docs/smt-results.schema.json) |
| `report` | what the run ran with, and the counts of its rows | [`smt-report.schema.json`](https://github.com/OmnibusCloud/Controllers/blob/main/AI/OutWit.Controller.AI.Smt/docs/smt-report.schema.json), `smt-report/1` |
| `integrity` | what the server did with what the nodes returned | [`smt-integrity.schema.json`](https://github.com/OmnibusCloud/Controllers/blob/main/AI/OutWit.Controller.AI.Smt/docs/smt-integrity.schema.json), `smt-integrity/1` |
| `provenance` | what the run ran with: the profile, the module, the engine, the controller and its model package, the options, the set | [`smt-provenance.schema.json`](https://github.com/OmnibusCloud/Controllers/blob/main/AI/OutWit.Controller.AI.Smt/docs/smt-provenance.schema.json), `smt-provenance/1` |
| `ledger` | the server's own blob: the four documents and the full models of the rows the results cut. **Not for labs**: its format is the controller's and may change with any version | none |

### A row of the results and the row of the set

A row of the results is tied to the row of the set by `index`: the rows of the set counted from 0,
taken and refused, blank lines not. `id` is the row's id, and empty for a refused row whose id
could not be read (a line that is no JSON object, an id that is not one); so an application ties a
row by its index, never by its id. A refusal of the preflight names the line of the set instead
(`Row`, from 1, blank lines counted). In the example set below, the row after the blank line has
the index 3 and is on line 5.

A row of the results is not a node's row. A node returns `SmtResultData` rows (the model
package's *A node's work*) with the statuses `ok`, `refused`, `input_error`, `output_exceeded`,
`engine_crash` and `node_fault`; the server takes them in, settles each problem on its copies, and
writes one row of the results a problem, whose statuses are `ok`, `refused`, `input_error`,
`output_exceeded`, `engine_crash`, `no_result` and `disputed`: a node's fault is never a row of the
results.

| Fields of a row of the results | Come from |
|---|---|
| `index`, `id`, `query`, `expected`, `tags` | the row of the set |
| `logic` | the problem as the server's sanitizer took it |
| `status`, `code`, `message`, `verdict`, `reason`, `count`, `complete`, `limit_reached`, `proof`, `model`, `values`, `models`, `digest`, `stats` | the settled answer: the node's row that the copies settled on. The model texts cut at 65,536 bytes |
| `status` `refused` with `code`, `message`, `line` | the set's refusal of the row: no node saw it |
| `status` `no_result` or `disputed` with `code` | the server: no answer was taken, or the copies did not settle |
| `expected_agreement`, `validation`, `validation_reason`, `validation_detail`, `model_truncated`, `copies`, `agreed`, `settled_by` | the server |

A node's row also carries the problem's hash, the ticket of its package, the solver's own word of
an `unknown` (`ReasonText`), the peak of its memory, the last 8 KB of its transcript, the
controller version and the platform: none of them is in the results.

### Every code

`code` is a fixed word; which words a status has is the capabilities' `results.codes`. `ok`,
`input_error` and `output_exceeded` have none (the empty word).

| Status | Code | |
|---|---|---|
| `refused` | `syntax_error` | the text is not SMT-LIB |
| `refused` | `text_too_long` | the problem has more characters than a problem may |
| `refused` | `too_many_tokens` | more tokens than a problem may have |
| `refused` | `nesting_too_deep` | nested deeper than a problem may |
| `refused` | `numeral_too_long` | a number with more digits than a problem may hold |
| `refused` | `literal_too_wide` | a hexadecimal or binary literal wider than a problem may hold |
| `refused` | `string_too_long` | a string literal longer than a problem may hold |
| `refused` | `not_a_command` | a top-level form that is no command |
| `refused` | `command_not_allowed` | a command that is neither kept nor stripped (`push`, `set-option`, `echo`, ...) |
| `refused` | `command_after_end` | a kept command after `check-sat` or `exit` |
| `refused` | `command_malformed` | a kept command in another shape than the standard's |
| `refused` | `reserved_symbol` | a reserved word between bars |
| `refused` | `ambiguous_token` | a symbol that starts with a minus and a digit (`-5`) |
| `refused` | `non_ascii_string` | a string literal with a character outside printable ASCII |
| `refused` | `index_too_large` | an index above 65,536 (a bit-vector's width, an extension, ...) |
| `refused` | `too_many_symbols` | more than 100,000 declared names |
| `refused` | `reserved_prefix` | a symbol that starts with `oc!`, the controller's own prefix |
| `refused` | `not_a_term` | a term (`goal`, `values`, `project`) that is no single s-expression |
| `refused` | `builtin_name` | a function named like an operator the solver reads as its own, under the logic `ALL` or none |
| `refused` | `row_malformed` | the line is not one JSON object of valid UTF-8, or gives a field twice |
| `refused` | `row_too_long` | the line is longer than 16 MiB |
| `refused` | `id_invalid` | the id is not 1 to 64 of the characters an id is made of |
| `refused` | `id_duplicate` | an earlier row has the id |
| `refused` | `field_missing` | the row lacks `id`, `smt2`, or what its query needs (`project`, `goal`) |
| `refused` | `field_invalid` | a field's value is of another type, out of its range, or none of its words |
| `refused` | `field_not_allowed` | a name that is no field of a row, or a field of another query |
| `refused` | `unknown_constant` | a name of `project` is no constant the problem declares |
| `refused` | `too_many_problems` | the row after the 1,000,000th: it and the rest were not read |
| `refused` | `set_too_large` | the line that passes 4 GiB: it and the rest were not read |
| `engine_crash` | `trap` | the solver trapped: no budget stopped it |
| `engine_crash` | `exit` | the solver ended by itself with a status that is no answer |
| `engine_crash` | `engine_error` | the engine failed a call of the solver's without a trap or an exit |
| `engine_crash` | `unreadable_answer` | the solver answered a command with something that cannot be read as its answer |
| `no_result` | `node_refused` | every node given the problem refused it, which an honest node never does |
| `no_result` | `sandbox_unavailable` | the last node had no sandbox for the solver |
| `no_result` | `backstop` | the last session ran past the node's wall-clock backstop |
| `no_result` | `cancelled` | the last session was cancelled |
| `no_result` | `not_instantiated` | the module could not be started on the last node |
| `no_result` | `host_fault` | the last node failed outside the solver |
| `no_result` | `version_mismatch` | the last node runs another controller version |
| `no_result` | `package_invalid` | the last node took the package's reference for none |
| `no_result` | `ticket_invalid` | the last node took the package's ticket for none |
| `no_result` | `package_too_large` | the package was larger than the last node takes |
| `no_result` | `package_hash_mismatch` | the package's blob reached the last node with other bytes |
| `no_result` | `package_unreadable` | the last node could not read the package |
| `no_result` | `package_mismatch` | the package held other problems than its reference names, to the last node |
| `no_result` | `memory_insufficient` | the last node had no memory for one session |
| `no_result` | `download_failed` | the last node could not download the package |
| `no_result` | `upload_failed` | the last node could not upload its rows |
| `no_result` | `unknown_ticket` | the last result came under a ticket the round did not give out |
| `no_result` | `duplicate` | the last result was a result blob returned again |
| `no_result` | `unreadable` | the last result was not a package of rows the server reads |
| `no_result` | `content` | the last result held a row no session could have written, or not exactly its package's rows |
| `no_result` | `surplus` | more results came for the package than were asked for |
| `no_result` | `other_package` | the last result came back for another package's task |
| `no_result` | `no_answer` | no answer came back for the problem at all |
| `disputed` | `no_majority` | no answer was given by more machines than any other, and by two at least |
| `disputed` | `too_few_copies` | fewer than two of the three more machines answered |
| `disputed` | `surplus_copies` | the leading answer does not lead by two machines once the answers that may be one machine's second are left out |

The codes of a refusal in full, with what is looked at first, are the model package's
[*Refusal codes*](https://github.com/OmnibusCloud/Controllers/blob/main/AI/OutWit.Controller.AI.Smt.Model/README.md#refusal-codes)
and [*The codes of a row*](https://github.com/OmnibusCloud/Controllers/blob/main/AI/OutWit.Controller.AI.Smt.Model/README.md#the-codes-of-a-row).
`validation_reason` has words of its own: the model package's
[*What is partial*](https://github.com/OmnibusCloud/Controllers/blob/main/AI/OutWit.Controller.AI.Smt.Model/README.md#what-is-partial),
`assertion_false` and `value_mismatch` of a failed class, and the server's `output_exceeded` and
`budget_package`.

### What the user can do

| A row or the report says | What the user can do |
|---|---|
| `unknown` for the reason `rlimit` | raise `rlimit`: the check used the solver's counter up |
| `unknown` for the reason `fuel` | raise `hard_cap_s`: the problem used its hard cap up |
| `unknown` for the reason `memout` | raise `memory_mb`; a session then needs more memory on a node (the preflight's `SessionMemoryBytes`), and fewer nodes have it |
| `unknown` for the reason `incomplete` or `other` | nothing in the options: the solver gave up by its own word; another formulation of the problem |
| `output_exceeded` | the verdict stands and the model is lost: ask for fewer `values` or fewer models |
| `refused` | fix the row: `code` and `message` say what is wrong |
| `input_error` | fix the problem: `message` is the solver's own |
| `engine_crash` | report it: the same problem crashes the same way on every machine |
| `no_result` | run the problem again; `code` says what the nodes said last |
| `disputed` | run the problem again, and report it: machines computed otherwise |
| `partial` | the model is not checked to the end and rests on the copies: raise `sampling_rate` to have more of it on two machines |
| `failed` beside `agreement` | report it: the machines agree and the server's evaluator does not |
| `unsat` beside `one_copy` | it rests on one machine: raise `sampling_rate` (1 solves every package twice) |
| `disagree` | the verdict is the other one than the row or the problem's `:status` expects: check the expectation |
| `short` | a copy the sampling asked for was lost: run again on a larger pool |
| `complete` false, for the reason `other` | the solver did not take a found model's values back: more models may exist (capabilities.md, *Known limits*) |

### When a run is alarming

A run is alarming, and a person must look at it, when:

- one of the integrity document's `evaluator_solver_disagreements`, `lies`, `disputed` and
  `no_result` is above 0;
- one of the report's `expected.disagree` and `validation.classes.failed` is above 0 (or a row of
  the results says `disagree` or `failed`);
- the report's `run.sampling` is `short`;
- the report does not count the rows it was written from (its `statuses`, `verdicts`, `expected`
  and `validation.classes` against the rows of the results).

Worth showing, and no alarm:
`unsat_one_copy` and `complete_one_copy` (answers on one machine's word), partial classes, unknowns,
a `binding` other than `bound`, packages a node could not work on and refused results (the repair
round answers them). The live tool's exit code 3 is this statement.

### Reading a model

`model`, `values` and `models[].model`, `models[].values` are the solver's own text, SMT-LIB: the
answer of `(get-model)`, a list of `define-fun`, and of `(get-value (...))`, a list of pairs of a
term and its value. The server has checked them (`validation`); an application that wants to
check one itself reads it with `SmtModel.Read` and checks it with `SmtModelValidator` of the model
package, against the problem as `SmtSanitizer` takes it. When `model_truncated` is true the texts
of the row passed 65,536 bytes: whole texts only are shown, in the order the session gave them, and
the first that does not fit and every one after it are null. The server checked the full models;
they are in the run's ledger, which is not for labs.

### Sizes

The results are a line a row of the set: up to 1,000,000 lines, each with up to 65,536 bytes of
model text before JSON escapes it, beside a message of up to 1,024 characters, an id of up to 64
and tags of up to 256. The report grows with the set too: `long_tail.problems` lists every problem
with a settled row, and `unsat_one_copy`, `complete_one_copy` and the enumerations' `kept`,
`kept_one_copy` and `dropped` list problems by index; the integrity document lists every dispute
and every disagreement between the evaluator and the solver. An application reads the results a
line at a time and pages them, and reads the report and the integrity document with a streaming
reader on a large set.

### Reproducing a row

A row's `digest` (`smt-result/1`) is computed over the node's row: its index, id, the problem's
hash, the query, every word, every model and value whole, the counts, the cost, the peak of its
memory and its transcript. A lab does not have all of that, so it cannot recompute a digest from
the documents. What it can do: compare the digests of two runs whose provenance names the same
profile, controller, model package and options (the same problem at the same index with the same
id gives the same digest); check a `sat` model itself with the model package's evaluator; and run
the problem again. No tool to solve one problem outside a server is published.

### Time

No duration is estimated anywhere: how long an SMT problem takes cannot be told from its text. A
form may show the preflight's upper bound (the taken problems times the hard cap, in seconds of the
reference machine) and the capabilities' pool: `nodes`, `workers` (sessions at once) and `rate`, in
problems a second of the benchmark's own problems (`benchmark_unit`), which says how strong the pool
is and nothing of how long a lab's problems take.

### The example

The set: a check with the values of two terms, a check with no model, an enumeration, a proof, and
a row the sanitizer refuses, a blank line before the proof:

<!-- example: set -->
```json
{"id": "half", "smt2": "(set-logic QF_LIA)(declare-const x Int)(assert (= (* 2 x) 10))", "values": ["x", "(+ x 1)"], "expected": "sat"}
{"id": "odd", "smt2": "(set-logic QF_LIA)(declare-const x Int)(assert (= (* 2 x) 7))", "expected": "unsat"}
{"id": "pairs", "query": "enumerate", "smt2": "(set-logic QF_LIA)(declare-const a Int)(declare-const b Int)(assert (< 0 a b 4))", "project": ["a", "b"], "max_models": 5}

{"id": "double", "query": "prove", "smt2": "(set-logic QF_LIA)(declare-const x Int)(assert (> x 1))", "goal": "(> (* 2 x) x)"}
{"id": "push", "smt2": "(push 1)(declare-const x Int)"}
```

Run with `SamplingRate = 1` (every package on two machines) and the other options at their
defaults, on three machines:

<details><summary>The results</summary>

<!-- example: results -->
```json
{"index":0,"id":"half","query":"check","logic":"QF_LIA","status":"ok","code":"","message":"","verdict":"sat","reason":"none","expected":"sat","expected_agreement":"agree","values":"((x 5)\n ((\u002B x 1) 6))\n","model":"(\n  (define-fun x () Int\n    5)\n)\n","model_truncated":false,"validation":"validated","validation_reason":"","copies":2,"agreed":true,"settled_by":"copies","digest":"efce84caf173afda9258154675780b8d2783eece45187ce5cf766c7c9d019fb4","stats":{"resource_count":42,"fuel":21676065,"checks":1}}
{"index":1,"id":"odd","query":"check","logic":"QF_LIA","status":"ok","code":"","message":"","verdict":"unsat","reason":"none","expected":"unsat","expected_agreement":"agree","values":null,"model":null,"model_truncated":false,"validation":"none","validation_reason":"","copies":2,"agreed":true,"settled_by":"copies","digest":"ec2b09f8b5fff3e2467b3e1298170d325f446c6163683452d063587cd525066c","stats":{"resource_count":10,"fuel":21006093,"checks":1}}
{"index":2,"id":"pairs","query":"enumerate","logic":"QF_LIA","status":"ok","code":"","message":"","verdict":"sat","reason":"none","expected":"none","expected_agreement":"n/a","count":3,"complete":true,"limit_reached":false,"models":[{"values":"((a 1)\n (b 2))\n","model":"(\n  (define-fun b () Int\n    2)\n  (define-fun a () Int\n    1)\n)\n"},{"values":"((a 2)\n (b 3))\n","model":"(\n  (define-fun b () Int\n    3)\n  (define-fun a () Int\n    2)\n)\n"},{"values":"((a 1)\n (b 3))\n","model":"(\n  (define-fun b () Int\n    3)\n  (define-fun a () Int\n    1)\n)\n"}],"model_truncated":false,"validation":"validated","validation_reason":"","copies":2,"agreed":true,"settled_by":"copies","digest":"f9ee81c7ce16cc0c914813bf23afa609ba25fffa310110df7124177a780fa3e5","stats":{"resource_count":871,"fuel":29315376,"checks":4}}
{"index":3,"id":"double","query":"prove","logic":"QF_LIA","status":"ok","code":"","message":"","verdict":"unsat","reason":"none","expected":"none","expected_agreement":"n/a","proof":"proved","model":null,"model_truncated":false,"validation":"none","validation_reason":"","copies":2,"agreed":true,"settled_by":"copies","digest":"b2e70fd7d23dfabbbe5d1d7ac44459e6029b227ea54495578ec387de4b78c8e5","stats":{"resource_count":169,"fuel":24100421,"checks":1}}
{"index":4,"id":"push","status":"refused","code":"command_not_allowed","message":"incremental scripts, unsat cores and proofs are not supported","line":1,"verdict":"none","reason":"none","expected":"none","expected_agreement":"n/a","validation":"none","validation_reason":"","copies":0,"agreed":false,"settled_by":"","model_truncated":false}
```

</details>

<details><summary>The report</summary>

<!-- example: report -->
```json
{
  "schema": "smt-report/1",
  "run": {
    "profile": "z3-5.4.1",
    "module_sha256": "9b8a67e85542b5d62d30b40060dd237fcef07f794ea235483afc83eef47a1364",
    "controller": "1.0.0",
    "sampling_rate": 1,
    "sampling": "as_asked",
    "unanswered_packages": 0,
    "binding": "bound",
    "options": {
      "profile": "z3-5.4.1",
      "query": "check",
      "max_models": 2,
      "seed": 0,
      "rlimit": 150000000,
      "hard_cap_s": 60,
      "fuel": 300000000000,
      "memory_mb": 1024,
      "sampling_rate": 1
    }
  },
  "problem_set": {
    "rows": 5,
    "accepted": 4,
    "refused": 1,
    "refused_by_code": {"command_not_allowed": 1},
    "logics": {"QF_LIA": 4},
    "other_logics": 0,
    "queries": {"check": 2, "enumerate": 1, "prove": 1},
    "expected": {"none": 2, "sat": 1, "unsat": 1}
  },
  "statuses": {
    "ok": 4,
    "refused": 1,
    "input_error": 0,
    "output_exceeded": 0,
    "engine_crash": 0,
    "no_result": 0,
    "disputed": 0
  },
  "verdicts": {"sat": 2, "unsat": 2, "unknown": 0},
  "unknown": {"rlimit": 0, "fuel": 0, "memout": 0, "incomplete": 0, "other": 0},
  "expected": {"agree": 2, "disagree": 0, "n/a": 3},
  "validation": {
    "classes": {"validated": 2, "partial": 0, "failed": 0},
    "by_logic": {
      "QF_LIA": {"validated": 2, "partial": 0, "failed": 0}
    },
    "partial_by_reason": {}
  },
  "copies": {
    "two_or_more": 4,
    "agreed": 4,
    "disputed": 0,
    "disputes": {"resolved": 0, "no_majority": 0, "too_few_copies": 0, "surplus_copies": 0},
    "settled_by": {"one_copy": 0, "copies": 4, "dispute": 0, "agreement": 0}
  },
  "unsat_one_copy": {
    "count": 0,
    "indices": []
  },
  "complete_one_copy": {
    "count": 0,
    "indices": []
  },
  "long_tail": {
    "rlimit": 150000000,
    "fuel": 300000000000,
    "problems": [
      {"index": 2, "resource_count": 871, "fuel": 29315376, "stopped_by": ""},
      {"index": 3, "resource_count": 169, "fuel": 24100421, "stopped_by": ""},
      {"index": 0, "resource_count": 42, "fuel": 21676065, "stopped_by": ""},
      {"index": 1, "resource_count": 10, "fuel": 21006093, "stopped_by": ""}
    ]
  },
  "attribution": {
    "sources": [],
    "other_problems": 0
  },
  "enumerate": {
    "exactly_one": 0,
    "two_or_more": 1,
    "no_model": 0,
    "undecided": 0,
    "kept": [],
    "kept_one_copy": [],
    "dropped": [2]
  }
}
```

</details>

<details><summary>The integrity document</summary>

<!-- example: integrity -->
```json
{
  "schema": "smt-integrity/1",
  "summary": {
    "problems": 4,
    "copies_made": 8,
    "compared": 4,
    "agreements": 4,
    "disagreements": 0,
    "one_copy": 0,
    "unsat_one_copy": 0,
    "complete_one_copy": 0,
    "disputes": 0,
    "disputes_resolved": 0,
    "disputed": 0,
    "dispute_causes": {"digests_differ": 0, "model_failed": 0, "surplus": 0},
    "dispute_outcomes": {"resolved": 0, "no_majority": 0, "too_few_copies": 0, "surplus_copies": 0},
    "lies": 0,
    "problems_with_lies": 0,
    "failed_models": 0,
    "evaluator_solver_disagreements": 0,
    "repairs": 0,
    "repairs_by_reason": {},
    "no_result": 0,
    "no_result_by_reason": {},
    "rejected_results": 0,
    "rejected": {
      "unknown_ticket": 0,
      "duplicate": 0,
      "unreadable": 0,
      "content": 0,
      "surplus": 0,
      "other_package": 0
    },
    "node_faults": 0,
    "node_faults_by_code": {
      "version_mismatch": 0,
      "package_invalid": 0,
      "ticket_invalid": 0,
      "package_too_large": 0,
      "package_hash_mismatch": 0,
      "package_unreadable": 0,
      "package_mismatch": 0,
      "sandbox_unavailable": 0,
      "memory_insufficient": 0,
      "download_failed": 0,
      "upload_failed": 0
    },
    "sampling_rate": 1,
    "sampling": "as_asked",
    "unanswered_packages": 0,
    "binding": "bound",
    "results_bound": 2,
    "results_unbound": 0
  },
  "evaluator_solver_disagreements": [],
  "fan_outs": [
    {
      "round": "main",
      "packages": 1,
      "problems": 4,
      "copies": 2,
      "selected": 1,
      "complete": 1,
      "unanswered": 0
    },
    {
      "round": "repair",
      "packages": 0,
      "problems": 0,
      "copies": 2,
      "selected": 0,
      "complete": 0,
      "unanswered": 0
    },
    {
      "round": "dispute",
      "packages": 0,
      "problems": 0,
      "copies": 3,
      "selected": 0,
      "complete": 0,
      "unanswered": 0
    }
  ],
  "disputes": []
}
```

</details>

## 4. What is promised

### The versions of the documents

Each document names its format: `smt-capabilities/1`, `smt-report/1`, `smt-integrity/1` and
`smt-provenance/1` in its `schema` field; the rows of the results are those of the report they came
with. Every schema is closed: no field beside the ones it lists, and every list of words an enum.
So under one name nothing is added, removed or renamed, and no word is added to a list: a document
that changes any of them is written under the next name (`/2`), and a schema's descriptions alone
may be made clearer under the same name. A controller writes one name of each document. An
application reads the `schema` field first and says plainly when it meets a name it does not know.
The schemas' `$id` is a name, not an address: the files are under `docs/` in the repository and
in the package.

The document types of part 1 are held the same way: `smt.options@1`, `smt.preflight@1`,
`smt.diagnostics@1`, `smt.count@1` and `smt.refusal@1` keep their members under their ids, and a
type whose members change is published under the next major (`@2`).

### Determinism

A row is a function of the row of the set (its index, its id, the problem's canonical text and so
its hash, its query and the terms beside it), of the options as resolved (`seed`, `rlimit`,
`hard_cap_s`, `memory_mb`, `max_models`) and of the solver module (the profile): the same on every
honest machine, to the byte. Not an input: the platform of the node, its cores and memory, how
many sessions it ran at once, which nodes and how many, the ticket, the time, the sampling rate.
The controller version and the model package's are not in a digest either, and still a version
may change one: a new module, a sanitizer that writes a problem's canonical text otherwise, other
commands sent around a problem (they are in the transcript). So digests are compared between runs
whose provenance agrees.

### Copies need Grid 1.4.0

Two answers count as two machines only when the platform told the server that they came back for
two tasks of one package in one fan-out: Grid 1.4.0 and later tells it, and the controller's
manifest names Grid 1.4.0 as the scripts' dependency. On a server whose Grid tells nothing, a run
still completes and compares its copies by digest, but no problem is settled `copies`, and the
report and the integrity document say `binding: unbound` (`mixed` when some results were told and
some not, `none` when no result came back). Nothing says before a run which Grid the server has: an application reads `binding`
after it, expects `bound`, and shows anything else as a weaker guarantee.

### Sampling on small sets

The sampling rate is a share of the packages, not of the problems, rounded up to a whole package:
any rate above 0 solves one package twice at least, so a small set has more of its problems twice
than the rate says, and a set of one package (up to 16 problems while a run is small) is solved
twice whole. Two copies need two machines: on a pool of one, nothing is solved twice, and the
documents say the sampling came out `short`. At 0 nothing is solved twice, every answer rests on
one machine, and the documents say `none`.

## 5. The live tool, and what to take from the documents instead

The controller's live tool, `smt-live`, is a developer's: it is never packed or published, and it
prints for people (it has no output for a program). What it checks, an application takes from the
documents and this page: each document held to its schema; the words of the capabilities
document for legends and filters; *When a run is alarming* above for the alarms; and, to compare
two runs, the digests row by row and the provenance whole.
