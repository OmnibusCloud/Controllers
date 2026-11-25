# OutWit.Controller.AI.Smt

SMT problems solved on anonymous nodes by a pinned solver, with every answer reproducible: Z3
runs as a WebAssembly module inside the controller's own wasm sandbox, under budgets that are
counts and not clocks, so the same problem gives the same verdict, the same model and the same
cost on every machine.

**Status: 0.5.0, in development; nothing is published.** This version holds the solver (the
profile, the sandbox, one session per problem), the node's side of a run (packages of problems
solved on machines of 8 GB at least, the packages of a node's share on one budget of sessions,
packages of rows returned, the node measured for the allocator) and the host's side: a lab's problem set cut into packages, solved
with copies on different machines, repaired (the copies the sampling asked for restored),
disputed, settled by the digests of the rows, every `sat` model checked on the server by the model
evaluator, and written out as results, a report, provenance and integrity, all through blobs
(`SmtRun`). The solver module is a data asset of the controller. What a lab can rely on, and the
limits, are [`docs/capabilities.md`](docs/capabilities.md). The language layer and
the wire types (the reader, the sanitizer, the model evaluator, the problem set, the options, the
packages and their rows, the preflight, the words of the documents) are
[`OutWit.Controller.AI.Smt.Model`](../OutWit.Controller.AI.Smt.Model/README.md); the scripts travel
in [`OutWit.Controller.AI.Smt.Scripts`](../OutWit.Controller.AI.Smt.Scripts/README.md).

| Activity | Where | In, out |
|---|---|---|
| `Smt.SolvePackage` | node | an `SmtTaskPackage` (a reference to a blob of problems), an `SmtResultPackage` (a reference to a blob of rows) |
| `Smt.Split` | host | the problem set (`Blob`) and the options (`SmtOptions`), an `SmtTaskPackageCollection` |
| `Smt.SamplingRate` | host | the options, a `Double`: the share of the packages solved on two machines |
| `Smt.Repairs` | host | the first fan-out's packages and results (and the rate), the packages of the problems without an answer or short of the copies asked for |
| `Smt.Disputes` | host | the first fan-out's and the repair round's packages and results, the packages of the problems that go to three more machines |
| `Smt.Settle` | host | the set, the options, every round's packages and results, and the rate; the ledger (`Blob`) |
| `Smt.CollectResults`, `Smt.Collect`, `Smt.Provenance`, `Smt.Integrity` | host | the ledger; the results ([`docs/smt-results.schema.json`](docs/smt-results.schema.json)), the report ([`docs/smt-report.schema.json`](docs/smt-report.schema.json)), the provenance ([`docs/smt-provenance.schema.json`](docs/smt-provenance.schema.json)), the integrity document ([`docs/smt-integrity.schema.json`](docs/smt-integrity.schema.json)) |
| `Smt.Preflight` | host | the set and the options, an `SmtPreflight` |
| `Smt.RuntimeDiagnostics` | host | nothing, an `SmtRuntimeDiagnostics` |
| `Smt.Capabilities` | host | nothing, a `Blob` of JSON ([`docs/smt-capabilities.schema.json`](docs/smt-capabilities.schema.json)) |

The variables `SmtTaskPackage`, `SmtResultPackage`, `SmtRuntimeDiagnostics`, `SmtOptions`,
`SmtPreflight` and the collections `SmtTaskPackageCollection`, `SmtResultPackageCollection` are
registered with them. The module depends on the Variables controller; the scripts use Grid.

## The profile

One solver, pinned (`SmtProfile`):

| | |
|---|---|
| Id | `z3-5.4.1` |
| Solver | Z3 5.4.1 built for `wasm32-wasip1`, single-threaded, with C++ exceptions as WebAssembly exceptions |
| Module | `wasm/z3-5.4.1.wasm`, 30,204,160 bytes, SHA-256 `9b8a67e85542b5d62d30b40060dd237fcef07f794ea235483afc83eef47a1364`; built from pinned sources by the workflow "AI Smt toolchain" ([`toolchain/`](../OutWit.Controller.AI.Smt.Assets/toolchain)), two builds compared byte for byte; a data asset of the controller: the archive `smt-z3-wasm.zip` of the release `smt-z3-v5.4.1` (30,314,801 bytes, SHA-256 `3c3f67877e4e193dc64070023fbf64addb4b01520de4b9fa79b673b77d48cc02`, with Z3's licence and notices), extracted at the module's root |
| Resource limit of one check | 150,000,000 units of the solver's own counter, when the options name none; at most 4,294,967,295 |
| Hard cap | seconds of a reference machine, turned into fuel at 5,000,000,000 units a second: 60 s are 3·10¹¹ units, counted from the solver's first instruction |
| Memory | the option's megabytes are the solver's own ceiling; the sandbox's cap of its linear memory is a quarter above it, and 16 MiB above it at the least (at most what a wasm32 program can address), so the solver meets its own ceiling first and ends cleanly. Under a ceiling too small for it to start in, it still starts, and ends by that ceiling at once |
| Output | 16 MiB for everything one session writes; the write that passes it ends the session |
| Wall clock | a backstop only: a minute and ten times the time the fuel takes at the reference rate. A session it ends says something about the machine, never about the problem |

A number of the options that is 0 means the default. A profile id the controller does not have
is refused by name.

## The sandbox

`SmtSandbox` is the controller's own copy of the wasm host the AI controllers share as source
(`AI/Shared/Wasm`), with an engine that takes WebAssembly exceptions: the solver is a C++
program that throws. That engine compiles one module, the pinned one, held to its size and its
SHA-256 before a byte of it is compiled. A session sees no folder of the machine, no network
and no clock but the host's pinned one; its stdin is a conversation with the controller, its
stdout and stderr are bounded streams of the host.

The module is compiled once per process, when the sandbox is first asked for: tens of seconds,
outside the budget of any session. `SmtSandboxHost` holds the process-wide sandbox, or the
reason, in fixed words, why this process has none.

## One session

`SmtSolver.Solve(problem, options, cancellationToken)` takes one problem with its query and gives
one outcome. It throws for nothing a problem or the solver holds.

1. The options are checked, then the problem's text is sanitized again and held to the hash it
   came with; the terms beside it (the values asked, the constants projected on, the goal) are
   read again too. The solver is given only what this machine's own sanitizer took.
2. A fresh instance of the solver is started, and the controller holds a conversation with it:
   each time the solver has read everything it was sent, the controller sees what it answered
   and sends the next command. One answered command goes out at a time, so an answer is the
   text between two reads: nothing has to be searched for in it, and nothing a model holds can
   end it early.

| Query | What is sent after the options, the problem and the resource limit |
|---|---|
| `check` | `(check-sat)` and the statistics; after `sat` the model and the values of the terms the row asks for; after `unknown` the reason |
| `enumerate` | `(check-sat)` and the statistics; after `sat` the values of the projected constants and the model, then the assertion that not all of those constants have those values, and the next check; until the number of models is reached, or a check says `unsat` (the count is exact) or `unknown` |
| `prove` | the negated goal with the problem, then as `check`: `unsat` is proved, `sat` is a counterexample with its model |

No `set-info` of a problem reaches the solver, and no command of the problem's own asks it to act.

## The outcome

| Status | When |
|---|---|
| `ok` | the query was answered: `sat`, `unsat`, or `unknown` with its reason |
| `refused` | the options or the problem were not taken; no solver ran. The code is the sanitizer's, or `hash_mismatch`, `profile_unknown`, `options_invalid` |
| `input_error` | the solver reported an error in what it was given, or said anything at all to a statement (a logic it does not know); its message is kept, cut to 512 characters |
| `output_exceeded` | the session wrote more than its output cap. The verdict of a check the solver had answered by then stands: it said `sat`, and it is the model that did not fit |
| `engine_crash` | a trap, an exit status that is no answer, an answer that cannot be read |
| `node_fault` | the machine, not the problem: no sandbox, the wall-clock backstop, a cancelled session, a file the host could not use |

| Reason of an `unknown` | When |
|---|---|
| `rlimit` | the resource limit ended the check: the solver says `canceled` or `max. resource limit exceeded`, and its counter stands at the limit |
| `fuel` | the session's fuel ran out |
| `memout` | the solver was refused memory: its exit status 101 with its line `(error "out of memory")`, or the sandbox's cap of the heap its exceptions live in |
| `incomplete` | the solver's own word |
| `other` | any other word of the solver's, which the outcome keeps |

Beside them the outcome holds, for an enumeration, the count and whether it is complete; for a
proof, proved or counterexample; the models and the values as the solver printed them, for the
evaluator; the solver's resource counter, the fuel used, the peak of the linear memory, the
number of checks, and the last 8 KB of the transcript (the commands sent, without the problem's
text, and the answers).

Everything in an outcome but the measured wall time is a function of the problem, the options
and the module: the tests hold forty-one problems to one file of hashes and fuel on Windows,
Linux and macOS, hold a host with explicit trap checks to one with hardware traps, and hold
eight sessions at once to the same sessions one by one.

## A package on a node

Problems travel to a node and rows travel back as blobs; the messages that assign the work and
report it carry references only. `Smt.SolvePackage` is given an `SmtTaskPackageData` and:

1. holds the reference to what a node takes, before anything is downloaded: the controller
   version the job runs on is this node's, the ticket is 32 lower-case hex characters, there is
   a blob, there are options, 1 to 256 problems, a blob of at most 257 MiB;
2. downloads the blob over the node's blob channel (in chunks, resumed after a break, tried again
   for about five minutes; a few downloads of a share at once, the first transfer of all alone);
3. holds the blob to its reference: its size, its SHA-256, and that it holds exactly the problems
   the reference names (their number and the digest of their indices);
4. waits for its turn on the node's budget of sessions (below), then makes the process's
   sandbox (the module is compiled at the first package of a process, before any session starts)
   and reads the degree its options get;
5. solves its problems on the budget's sessions, beside the problems of the other packages of
   the process, and writes each row to its own package of results as its session ends, in the
   order the sessions end;
6. uploads the rows and returns an `SmtResultPackageData`: the blob, its SHA-256 and size, the
   number of rows and the package's ticket.

The client starts every package of a node's share at once; their problems share the node's
sessions (below). A problem that fails on its own (a refusal, an error of the solver's, a
crash, a budget) is a row with its status, never a failed package. A package the node cannot work
on at all comes back without a blob and with the code of why (`SmtNodeFaults`): another version,
a reference that is not one, a ticket that is none, a blob too large, of another hash, unreadable
or holding other problems, no sandbox, no memory for one session, a download or an upload that
failed at every attempt. The words of such a code name versions, sizes and hashes, never a
path or a name of the machine. A failure of the node's own that has no code fails the activity in
fixed words, and the node's log says which.

### The package format

An eight-byte header (`WSPK`, the kind, the version, two zero bytes) and a Brotli stream of
frames, each the length of a MemoryPack image and the image: one frame a problem in a package of
problems, one frame a row in a package of results. A reader holds every frame to its bound
before it holds it (128 MiB a problem, 64 MiB a row), a package to the number of frames its
reference names and to the bytes it may unpack to (256 MiB a package of problems), and grows a
frame's buffer with the bytes that arrive, never with the length a frame claims.

### The budget of sessions and the degree

The sessions run on threads of their own below normal priority, each with a stack that holds a
guest's. The degree is `min(logical cores, 16, sessions the memory holds)`. One
session may take twice the host's cap of the solver's linear memory (the heap of the solver's
exception objects is capped apart from the linear memory and may take as much again) and a
margin of 192 MiB for the host's part of a session (its output, its answers as text, the problem
as the sanitizer took it, the row being written, the guest's stack); the sessions may claim three
quarters of the memory the machine has available as the runtime sees it. Under the default
options a session counts 2,752 MiB: a machine with 4 GiB available runs one, with 16 GiB four,
with 64 GiB sixteen (seventeen fit). A machine where not one session fits refuses the package
(`memory_insufficient`), and the repair round sends its problems out again.

A process keeps one budget of sessions for every package it solves (`SmtSessionBudget`), so a
package's slowest problem does not hold the node: a session that ends goes to the next problem
waiting, of the same package or of the next one.

- **Sessions and memory.** At most `min(logical cores, 16)` sessions run at once, and together they
  claim at most three quarters of the memory the machine had available, each counted at its
  package's session size. For packages of one size that is the degree above to the session.
  Packages of different sizes (of different jobs on one node) share the same bytes: a large
  session takes the room of several small ones, and no mix of them claims more than the memory
  holds.
- **Order.** Problems are handed out in the order their packages were let in, a package's own in
  its order; the first problem waiting goes first and nothing overtakes it, so a large session
  waiting for room is not starved by small ones behind it.
- **Open packages.** A package waits for its turn with its blob downloaded, and is let in (its blob
  checked again, read a problem at a time, its result package written) only when the packages
  open have handed out all of their problems: the next package is ready while the last problems
  of the others run. Every open package but the newest has a session running, so no more are
  open than the sessions running and one, and at most 16. A package's blob is closed with its
  last problem; its result package (a Brotli encoder, about 11 MiB) stays open until its last row.
- **The machine.** The cores and the available memory are read when a package is let in and no
  other package is open (the first package of a share, after the module is compiled); while
  packages are open the reading stands, since what the machine has available then counts the
  sessions running.

A row does not depend on any of that: every problem has a session of its own, and its fuel is a
count. A package returns exactly its own rows, under its own ticket, whatever ran beside it.

### The machines a package goes to

Smt needs machines of 8 GB at least. `Smt.SolvePackage` declares
`[RequiresResources(MinRamMb = 7680)]` (`SmtSandboxDegree.MIN_RAM_MB`), and the server's planner
sends a package only to a node whose reported memory meets it. One session under the default
options needs 3,670 MiB available (2,752 MiB at three quarters): an 8 GB machine has that with the
node's client and its system running; a 6 GB machine has about 2 GB available then, and refuses
every package. A node reports the memory its system sees, and a machine sold as 8 GB sees
somewhat less than 8,192 MiB (Windows leaves out what the firmware and the graphics reserve,
Linux reports its `MemTotal`): 7,680 MiB (7.5 GiB) lets such machines in and keeps the 6 GB ones
(about 5,900 MiB) out. Where one session does not fit at a given moment, the node's own check
stays: it refuses the package, and the repair round sends its problems out again, to two
machines; so does a node that has reported no capabilities, which the planner admits unchecked.
The capabilities document states the number (`node.min_ram_mb`), and its pool counts only the
nodes the planner would send a package to.

### Cancel

Once the job is cancelled no problem is handed out any more, every running session ends at the
solver's next read of its input, and the package returns nothing: it is sent again elsewhere. A
session inside a check ends when the check does: by its answer, its resource limit, its fuel or
the backstop. Fuel ends it within the hard cap on a machine at the reference rate (60 s by
default, 600 s at most); on any machine the backstop ends it within a minute and ten times the
hard cap (660 s by default, 6,060 s at most). A check whose resource counter does not move
(non-linear real arithmetic can burn its fuel so) is the case that waits that long.

### Rows and their digest

Each row (`SmtResultData`) carries the problem's index, id and the hash of the text the node was
given, the ticket, every value of the session's outcome in the words of a result, all the models,
the controller version and the platform (`win-x64`, `linux-x64`, `osx-arm64`), the wall time, and
the digest of its canonical form, which leaves the wall time, the platform, the version and the
ticket out. Two honest machines give the same digest for a problem: the tests hold the rows of
the forty-one problems to one committed file of digests on the three platforms, in both trap
modes, twice in a row, at eight sessions at once, and solved through the engine on two nodes.

## Diagnostics, capabilities and the benchmark

`Smt.RuntimeDiagnostics` says what the controller of the answering process carries and can do:
its version and profile, the module's pin and whether the process has it and has compiled it
(with the compile time), the engine library's pin and whether it is the one loaded, the trap mode,
the platform, and the degree a package would get, with what decided it. Asking compiles nothing.
`Smt.Capabilities` writes what a lab can know before a run as a JSON document
([`docs/smt-capabilities.schema.json`](docs/smt-capabilities.schema.json)): the profile, the
queries, the defaults and ceilings of the options and of a problem, the words of a row and the
codes of a node's refusal, the size of a package, the degree rule and the least memory of a
machine, the benchmark's unit, the platforms and the pool as totals. Nothing in either names a
machine.

The benchmark of `Smt.SolvePackage` gives the allocator a rate: thirty small problems written for
it (every query; linear integers and reals, bit-vectors, uninterpreted functions, arrays,
datatypes, strings), the same on every node, solved by one pool at the node's degree, as the
budget runs the problems of a share, the set given often enough that each session gets about
four problems a run. A
warm-up run, then timed runs; the rate is problems a second of the median run, in the unit
`smt-problem@ref-v1`. Every problem is held to its outcome and its fuel, the same on every
machine, so a node that computes otherwise cannot score; a node without a sandbox or without
memory for one session reports the default (unranked) score.

## A run on the host

`SmtRun(Blob:problems, SmtOptions:opts)` follows Verify's run:

```
Smt.Split -> Smt.SamplingRate -> Grid.Replicas(2, rate) -> Grid.ForEach => Smt.SolvePackage
          -> Smt.Repairs -> Grid.Replicas(2) -> Grid.ForEach => Smt.SolvePackage
          -> Smt.Disputes -> Grid.Replicas(3) -> Grid.ForEach => Smt.SolvePackage
          -> Smt.Settle -> Smt.CollectResults, Smt.Collect, Smt.Provenance, Smt.Integrity
```

The work is split once and sent once; the repair round and the dispute round are the only second
sends, and both cost traffic on the fault path only. Nothing names, avoids or steers to a machine:
copies on different machines are `Grid.Replicas`' only, within one fan-out; Grid guarantees
nothing across fan-outs.

**Split.** The set is read a row at a time by the model package's reader, the taken problems wait
in a working file of the host's until the run knows how many it has, and are cut into packages
(`SmtPackagePlanner`): 16 problems a package while the run is small, about 1,024 packages once it
is large, never more than the 256 a node takes, and a package ends before its problems' text
passes 32 Mi characters. The cut depends on the problems alone. A set without a row, options out
of range and a profile the controller does not have are refused; a set whose every row is
refused gives no package, and its results say why for each row.

**Intake.** A node's result package is a claim. It is taken only under a ticket its round gave
out. A package with a fault code is the node's word that it could not work on the package (one of
`SmtNodeFaults`, no blob), and is counted so. Otherwise its blob must be the bytes its reference
names, a package of rows of this format, holding exactly the rows of its package, each once, each
one a node may give (`SmtRowChecks`): its ticket, its problem's id, hash and query, this
controller version, a platform the controller ships for, the words of a row, the counts of models
and values a session of its query gives, texts within what a session writes, and a digest that
is its own. Anything else fails that package and is counted by reason (`unknown_ticket`,
`duplicate`, `unreadable`, `content`, `surplus`). Rows wait in a spool file, never together in
memory.

**Settling** (`SmtRulings`), problem by problem in the set's order:

- The first round's answers are ruled on (a row of a fault of the node is no answer). Two kinds of
  problem go out once more, in the repair round, which asks two machines for every package
  (`Grid.Replicas(2)`): a problem with no answer, and a problem of a package the first round asked
  for twice that came back with fewer than two answers (a copy a node refused with its fault code,
  a copy whose result package was refused, a row of a fault of the node; `copies_short` in the
  integrity document's `repairs_by_reason`). A package is known to have been asked for twice by
  the two results that came back under its ticket, whatever they say, or by a rate that asks for
  every package; one of which at most one result came back cannot be told from a package asked
  for once, and leaves the sampling `short`. A problem with no answer whose package was not
  sampled gets two copies in the repair round as well: the fault path pays for it. A problem
  with no answer after the repair round is `no_result`, with the code of what the nodes said last.
- Copies are compared by their digests and by nothing else. Answers of one digest are one answer:
  `copies` when two of them come from one fan-out (the first, or the repair round), whose copies
  Grid keeps on different machines; `one_copy` otherwise. The lone answer of the first round and a
  lone answer of the repair round are never counted as two machines: two fan-outs may place them
  on one. The lone answer is still compared with the repair round's answers, and a difference is a
  dispute. The sampling is `as_asked` when every problem with an answer of a package asked for
  twice ends with two answers of one fan-out, and `short` when a repair pair lost a copy again.
- Every answer that says `sat` is checked by the model evaluator (`SmtHostEvaluator`) against the
  problem as the host's own sanitizer takes it: a check's model with the values it asked for,
  every model of an enumeration with its projected values, the counterexample of a proof against
  the problem and the negated goal. A model that did not fit the session's output is `partial`
  (`output_exceeded`); a row's class is the worst of its models.
- Answers of more than one digest, a package with more answers than were asked for, and an answer
  whose model is `failed` go to three more machines. The answer is the digest the most of them
  give, two at least, with no tie, and two machines' worth once the answers that may be one
  machine's second are left out (`dispute`); otherwise the problem is `disputed`.
- A failed model accuses nobody by itself: a copy is a lie only when it differs from the settled
  answer of the others. When the settled answer's model is still `failed`, the machines agree and
  the evaluator does not: the row says `validation: failed`, `settled_by: agreement`, and the
  integrity document lists the problem as a disagreement between the evaluator and the solver,
  with no machine counted as lying.

**Documents.** `Smt.Settle` writes the results, the report, the provenance and the integrity
document into one ledger blob of the host's own, with the full models of the rows whose models
the results cut at 64 KB; the other activities hand a document out of it as it is. No document
names a machine. Their fields are in [`docs/capabilities.md`](docs/capabilities.md) and their
schemas beside it.

**Preflight.** `Smt.Preflight` reads the set the same way, sends nothing, and gives an
`SmtPreflight`: the set counted, the options checked by name, the budget as numbers, the upper
bound "taken problems times the hard cap" (no duration is estimated anywhere), the memory of one
session on a node, and the packages a run would make.

## Licence

MIT. The module carries one assembly that is not the controller's own, the wasmtime binding:
see `THIRD-PARTY-NOTICES.txt`. The solver module, Z3, is MIT and brings its licence and its
notices in its own archive.
