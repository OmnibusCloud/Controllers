# Smt: what the controller can do

A reference for building on the Smt controller: what goes into a run and what comes out, what
is checked and what is not, and where the edges are. The [README](../README.md) explains how
things work inside; the schemas beside this page are the contract of the documents.

Version described: **Smt 0.5.0** (with Smt.Model 0.3.0 and Smt.Scripts 0.2.0). In development;
nothing is published.

## Contents

1. What Smt is for
2. At a glance
3. Calling Smt from an application
4. The problem set
5. What comes back
6. What a run checks, and what it does not claim
7. Limits
8. Scale and delivery
9. Known limits

## 1. What Smt is for

Solving many SMT problems (SMT-LIB 2.6, a declarative subset) on anonymous machines with every
answer reproducible: the solver is a pinned Z3 built for WebAssembly and run in the controller's
own sandbox, under budgets that are counts and not clocks, so the same problem under the same
options gives the same verdict, the same model and the same cost on every node. That is what lets
copies of a problem solved on different machines be compared to the byte, and what lets a lab
re-run a row and get the row.

Three queries: `check` (is the problem satisfiable, by which model or which values), `enumerate`
(the models told apart by a projection, up to a number, with a word on whether there are more),
`prove` (does a goal follow from the problem: proved, or a counterexample).

## 2. At a glance

| | |
|---|---|
| Solver | Z3 5.4.1 for `wasm32-wasip1`, single-threaded; profile `z3-5.4.1`; the module's SHA-256 is pinned and checked before it is compiled |
| Platforms of the nodes | Windows x64, Linux x64, macOS arm64 |
| Machines of the nodes | 8 GB of memory at least (7,680 MiB as a node reports it: a machine sold as 8 GB reports a little less than 8,192 MiB): the planner sends no package to a smaller one |
| Budget of a problem | the solver's resource limit of a check (150,000,000 by default) and the hard cap as fuel (60 s of a reference machine by default, at 5,000,000,000 units a second): counts, the same on every node |
| Copies | a share of the packages (the sampling rate, 0.05 by default) is solved on two machines; a problem whose package lost one of its two copies, or that has no answer, goes out again to two machines; a dispute on three more. Two answers are two machines only when the platform says they came back for two tasks of one fan-out (Grid 1.4.0 and later) |
| Checks | every `sat` model is evaluated against the problem on the server, by code that shares nothing with the solver |
| Documents | results (JSON lines), report, provenance, integrity (JSON), each with a schema |
| Machines | no document names one: a row says how many machines answered and whether they agreed |

## 3. Calling Smt from an application

### The scripts

| Script | Arguments | What it gives |
|---|---|---|
| `SmtRun` | `Blob:problems`, `SmtOptions:opts` | the blobs `results`, `report`, `provenance`, `integrity` |
| `SmtPreflight` | `Blob:problems`, `SmtOptions:opts` | `result`: an `SmtPreflight`; nothing is sent to a node |
| `SmtRuntimeDiagnostics` | none | `result`: an `SmtRuntimeDiagnostics`; `capabilities`: a JSON blob (the profile, the defaults and ceilings, the least memory of a node's machine, the pool as totals) |

The problem set and every document travel as blobs; the job's variables carry references and a
few numbers.

### The options (`SmtOptionsData`)

| Option | Default | Ceiling | |
|---|---|---|---|
| `query` | `check` | | the query of a row that names none |
| `max_models` | 2 | 1,000 | the models of an enumeration whose row names no number |
| `seed` | 0 | 2,147,483,647 | the seed of the solver's random choices |
| `rlimit` | 150,000,000 | 4,294,967,295 | the solver's own counter of work for one check |
| `hard_cap_s` | 60 | 600 | seconds of the reference machine for one problem, as fuel |
| `memory_mb` | 1,024 | 4,096 | the most memory of one problem |
| `sampling_rate` | 0.05 | 1 | the share of the packages solved on two machines; 0 none, 1 all |

A run whose options are out of range, or name a profile the controller does not have, is
refused before anything is sent. The documents state every option as the number the run used,
never the word "default".

### Before a run

`SmtPreflight` reads the set as a run reads it and says: the rows, taken and refused (by code, the
first 100 refusals in full: the line, the id, the command, the code and a reason that never
carries the problem's text), the taken problems by logic, by query and by expected verdict, the
commands the sanitizer left out, the licences, the SHA-256 of the set, what is wrong with the
options (one sentence an option, by name; the set is still read), the budget of a problem as
numbers, the memory a node needs for one session, the upper bound of the work (the taken problems
times the hard cap, in seconds of the reference machine), and the packages a run would make. No
duration is estimated: how long an SMT problem takes cannot be told from its text.

## 4. The problem set

JSON lines, one problem a line ([`smt-problems.schema.json`](../../OutWit.Controller.AI.Smt.Model/docs/smt-problems.schema.json)
of the model package): `id`, `smt2`, and the fields of the row's query (`values` for a check,
`project` and `max_models` for an enumeration, `goal` for a proof), `expected` and `tags`. The
reader, the sanitizer and their refusal codes are the model package's
([README](../../OutWit.Controller.AI.Smt.Model/README.md)): the same code reads a set in an
application, on the server and on the node. A refused row costs nothing and has its row in the
results.

## 5. What comes back

### The results ([`smt-results.schema.json`](smt-results.schema.json))

One JSON line a row of the set, in the set's order, refused rows included.

| Field | |
|---|---|
| `index`, `id`, `query`, `logic` | the row; `query` and `logic` for a taken problem only |
| `status`, `code`, `message` | `ok`, `refused` (by the set or the server's sanitizer), `input_error`, `output_exceeded`, `engine_crash`, or the server's `no_result` (no node answered, after the repair round either; `code` says what the nodes said last, `node_refused` when every node refused the problem) and `disputed` (copies disagreed and three more did not settle it; `code` is the dispute's outcome) |
| `verdict`, `reason` | `sat`, `unsat`, `unknown` (with `rlimit`, `fuel`, `memout`, `incomplete` or `other`), `none` |
| `expected`, `expected_agreement` | the verdict the row or the problem's `:status` expects, and `agree`, `disagree` or `n/a` |
| `count`, `complete`, `limit_reached`, `models` | an enumeration: the models found, whether the count is exact, whether the number asked for was reached, and each model with the values of the projected constants |
| `proof` | a proof: `proved` or `counterexample` |
| `model`, `values` | a check's model and the values it asked for; a proof's counterexample |
| `model_truncated` | the texts above are the solver's own and are shown up to 64 KB a row, whole texts only; past that they are null and this is true. The full models stay in the run's ledger, the server's own blob |
| `validation`, `validation_reason`, `validation_detail` | what the server's evaluator said of every model and of the values that came with it: `validated`, `partial` (with the reason), `failed` (`assertion_false` with the assertion, or `value_mismatch` with the value), or `none` when there is no model |
| `copies`, `agreed`, `settled_by` | how many answers the problem was ruled on, whether all gave one digest, and how the answer was settled: `one_copy` (it rests on one machine), `copies` (two machines of one fan-out at least: two answers the platform said came back for two tasks of the problem's package), `dispute`, `agreement` |
| `digest`, `stats` | the digest of the settled row (the same on every honest machine), its resource count, fuel and checks |
| `tags` | passed through |

### The report ([`smt-report.schema.json`](smt-report.schema.json))

What the run ran with in short (profile, module SHA-256, controller version, sampling rate and
whether it was met, whether the platform said which package each result came back for
(`binding`), the options as numbers); the set as it was read (rows, taken, refused by code,
by logic, by query); the rows by status; the verdicts; the unknowns by reason; the agreement with
the expected verdicts; the evaluator's classes, by logic, and the partial ones by reason; the
copies (problems answered by two machines or more, how many agreed, the disputes and how they
ended, the answers by how they were settled); the problems answered `unsat` by one machine only,
by index (`unsat_one_copy`), and the enumerations that found models and say there is no other by
one machine only (`complete_one_copy`); the long tail (every problem with a settled row, the most fuel first, with what stopped
it and the budget); the attribution from the problems' `:source` and `:license`; and for a run
with enumerations, the problems with exactly one model (kept; those of them on one machine's word
in `kept_one_copy`) and those with two or more (dropped).

### The integrity document ([`smt-integrity.schema.json`](smt-integrity.schema.json))

Counts only, and records by problem index: the answers taken, the problems compared and how they
compared, the problems that rest on one machine (and of them the `unsat` ones and the complete
enumerations that found models), the disputes by
cause and by outcome, the answers a lie was found in and the problems they are of, the models the
evaluator called failed, **the problems on which the machines agree and the evaluator does not,
each by its index**, the repairs and why, the problems left without a result and why, the result
packages refused by reason, the packages nodes said they could not work on by code, what every
fan-out was asked for and what came back, and how many results came back bound and unbound, with
the run's `binding` (`bound`, `unbound`, `mixed`, or `none` when no result came back).

### The provenance ([`smt-provenance.schema.json`](smt-provenance.schema.json))

The profile and the module's SHA-256 and size, the engine's version and the SHA-256 of its native
library for each platform, the controller's version and its model package's, the options as
resolved, the sampling rate, and the problem set's SHA-256, size and rows.

## 6. What a run checks, and what it does not claim

What holds:

- **Every `sat` is checked.** Each model of an answer that says `sat` is evaluated against the
  problem as the server's own sanitizer takes it: a check's model, every model of an enumeration,
  the counterexample of a proof against the problem and the negated goal. `validated` means every
  assertion is true under the model: the problem is satisfiable whoever said so.
- **The values asked are checked against the model.** The server knows the terms a row asked the
  values of, and takes no machine's word for them. A machine's answer must hold as many values as
  terms were asked, each of its term, and each value must be the value the term has under the
  row's model (the projected constants of an enumeration under each of its models). A value that
  is another fails the row (`value_mismatch`, with the value's place among those asked) and sends
  the problem to three more machines; values that are not those asked leave it `partial`
  (`unreadable_values`). A model is checked by its own text: a value never mends a model that
  breaks the problem.
- **Copies are compared by digest.** A row's digest covers every value but the wall time, the
  platform, the controller version and the ticket; two honest machines give the same digest.
  Copies that differ go to three more machines, and the answer is the digest two of them at least
  give, with no tie.
- **A failed model, or a failed value, accuses nobody by itself.** It sends the problem to three
  more machines. A copy is a lie only when it differs from the settled answer of the others. When
  the machines agree and the evaluator still says `failed`, the row says so
  (`settled_by: agreement`) and the integrity document lists the problem; no machine is taken for
  a liar.
- **A node's word is a claim.** A result is taken only under a ticket its round gave out, as an
  answer to a problem of its package, from this controller version, and only as a row a session
  of the controller could have written: its words and its counts together as a session's outcome
  has them (a verdict only where a session claims one, a `sat` with its model, the code of its
  status and no other word), its numbers within the envelope of the run's options (fuel, memory,
  checks), its texts within what a session writes, and a digest of its own; anything else fails
  its package and is counted. A result the platform says came back for a task of another package
  than the one its ticket names (a node that answered one of its packages twice, in place of
  another) is refused (`rejected.other_package`), and the package it came back for goes out again
  as a package without an answer. A package a node could not work on, and a problem whose session a
  node could not end, go out once more, to two machines.
- **A node's refusal is no answer.** A node refuses a problem only for what the server has
  already checked with the same code before it made the package (the sanitizer took the text and
  gave its hash, the options and the terms beside the problem were held to their rules), so an
  honest node never refuses a problem the server took. A refusal goes out once more like a
  problem without an answer (`repairs_by_reason.node_refused`, or `copies_short` beside an answer
  of a package asked for twice); a problem every node refused has no result, with the code
  `node_refused`.
- **The sampling asked for is restored.** A problem of a package asked for twice that came back
  with one answer (the other copy refused by its node, refused by the server, or a fault of the
  node) goes out again to two machines, and the integrity document counts it as `copies_short`.
  The run's `sampling` is `as_asked` when every such problem ends with two answers of one fan-out,
  and `short` when the repair pair lost a copy again.
- **No machine is named.** The documents name no machine, no user, no folder and no ticket.

What it does not claim:

- **`unsat` and `unknown` rest on the copies.** Nothing beyond the copies vouches for them; a
  problem whose package was not sampled rests on one machine, and the report lists every `unsat`
  answered by one machine only. Raise the sampling rate to 1 for every problem twice.
- **The completeness of an enumeration rests on the copies.** The models of an enumeration are
  checked; that there is no other (`complete: true`) is the solver's last `unsat`, and nothing
  checks it. The report lists every enumeration that found models and says it is complete on one
  machine's word (`complete_one_copy`), and of the enumerations kept as exactly one model, those
  that rest on one machine (`enumerate.kept_one_copy`).
- **`partial` is not checked to the end.** Quantifiers, algebraic numbers, regular expressions,
  floating point and the other reasons of the model package's evaluator leave a model partial: it
  rests on the copies too. A model past the session's output cap is partial (`output_exceeded`).
- **A value the evaluator does not compute is not vouched for.** A row that asks for the value of
  a term the evaluator leaves open (a division by zero, a selector of another constructor, a
  quantified term, the reasons above) is `partial` with that reason, whatever its model is: the
  value is shown as the solver printed it and rests on the copies.
- **Duplicate models of an enumeration are not looked for.** The count is the solver's.
- **Copies on different machines are the platform's.** The server sees results, not machines,
  and a result's ticket is the node's word. `copies` rests on the platform telling the server
  which package each result came back for: Grid 1.4.0 and later tell the return collection of
  every fan-out the package of each task, and the server refuses a result returned for another
  package than the one it names. Two answers count as two machines only when both came back bound
  in one fan-out, for two tasks of their package, which Grid keeps on different machines; Grid
  guarantees nothing across fan-outs, so an answer of the first round and an answer of the repair
  round with the same digest are `one_copy`. On an older Grid no answer is ever counted as a
  second machine: copies are still compared by digest, nothing is settled `copies`, and the
  documents say `binding: unbound`. A machine is what the platform knows as a node: two nodes of
  one owner are two machines to it.
- **A package asked for twice is known by its results.** Which packages a rate falls on is the
  platform's choice and the server never learns it: it knows a package was asked for twice when
  two results came back for it, whatever they say (a bound result for the package whose task it
  came back for, an unbound one under the ticket it names), or when the rate asks for every
  package. A package asked for twice of which at most one result came back cannot be found, is
  not sent again, and leaves the sampling `short`.

## 7. Limits

| | |
|---|---|
| Problems in a set | 1,000,000 rows |
| A problem | 4,194,304 characters, 2,000,000 tokens, nesting 100,000 (the model package's ceilings) |
| A package | 16 to 256 problems, about 1,024 packages once a run is large, at most 32 Mi characters of text; a node takes at most 256 problems and 257 MiB |
| A session's output | 16 MiB; past it the row is `output_exceeded` and keeps its verdict |
| A row a node returns | its models and values within the session's output (16 Mi characters together); 48 MiB and 64 KiB as bytes in its package (`packages.max_result_frame_bytes` of the capabilities) |
| What a lab sees of a row's models | 64 KB |
| What the server keeps of one run while it settles it | 128 GiB of the nodes' rows, 2 GiB of the full models of the rows whose models were cut; a run past either is not settled, and the job fails with `smt_rows_bound` or `smt_models_bound` |
| Memory of one session on a node | twice the host's cap of the solver's memory and 192 MiB: 2,752 MiB under the defaults |
| Memory of a node's machine | 7,680 MiB at least as the node reports it (a machine sold as 8 GB; `node.min_ram_mb` of the capabilities); the sessions may claim three quarters of what it has available |
| Sessions at once on a node | `min(logical cores, 16)`, and as many as three quarters of its available memory holds; shared by the problems of every package the node has, at most 16 packages open at once |
| Duration | none is estimated; the upper bound is the taken problems times the hard cap |

## 8. Scale and delivery

The set is read a row at a time and its taken problems wait in a working file of the server's
until they are cut into packages; packages, rows and documents travel as blobs and never sit
whole in the server's memory, which holds a small record a copy and a few numbers a problem. The
work is split once and sent once by the nodes' rates; the repair round and the dispute round are
the only second sends. A node solves the packages of its share on one budget of sessions: a
session that ends goes to the next problem waiting, of the same package or of the next one, so a
package's slowest problem does not hold the node's other sessions. The load test of the build server runs ten thousand problems through
`SmtRun` on three in-process nodes with the memory and every message measured: a request carries
one package reference, an answer one result reference.

## 9. Known limits

- A compile of the solver module costs a node tens of seconds at the first package of a process,
  outside every budget.
- A check whose resource counter does not move (non-linear real arithmetic) ends by its fuel or,
  on a slow machine, by the node's wall-clock backstop, which makes it a fault of the node and
  sends it once more.
- The report lists every problem of the long tail: its size grows with the set.
