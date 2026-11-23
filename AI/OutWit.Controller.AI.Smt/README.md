# OutWit.Controller.AI.Smt

SMT problems solved on anonymous nodes by a pinned solver, with every answer reproducible: Z3
runs as a WebAssembly module inside the controller's own wasm sandbox, under budgets that are
counts and not clocks, so the same problem gives the same verdict, the same model and the same
cost on every machine.

**Status: 0.2.0, in development; nothing is published.** This version holds the solver (the
profile, the sandbox, one session per problem) and the node's side of a run: a node solves
packages of problems with a pool of sessions and returns packages of rows, reports what it
carries, and measures itself for the allocator. The host's side of a run (cutting a problem set
into packages, the run's script, settling the copies, the report) is not here yet, and no script
is shipped. The solver module is not a data asset yet: it is looked for under `wasm/` in the
controller's folder, and a node without it says so. The language layer and the wire types (the
reader, the sanitizer, the model evaluator, the problem set, the options, the packages and their
rows) are [`OutWit.Controller.AI.Smt.Model`](../OutWit.Controller.AI.Smt.Model/README.md).

| Activity | Where | In, out |
|---|---|---|
| `Smt.SolvePackage` | node | an `SmtTaskPackage` (a reference to a blob of problems), an `SmtResultPackage` (a reference to a blob of rows) |
| `Smt.RuntimeDiagnostics` | host | nothing, an `SmtRuntimeDiagnostics` |
| `Smt.Capabilities` | host | nothing, a `Blob` of JSON ([`docs/smt-capabilities.schema.json`](docs/smt-capabilities.schema.json)) |

The variables `SmtTaskPackage`, `SmtResultPackage`, `SmtRuntimeDiagnostics` and the collections
`SmtTaskPackageCollection`, `SmtResultPackageCollection` are registered with them. The module
depends on the Variables controller.

## The profile

One solver, pinned (`SmtProfile`):

| | |
|---|---|
| Id | `z3-5.4.1` |
| Solver | Z3 5.4.1 built for `wasm32-wasip1`, single-threaded, with C++ exceptions as WebAssembly exceptions |
| Module | `wasm/z3-5.4.1.wasm`, 30,204,160 bytes, SHA-256 `9b8a67e85542b5d62d30b40060dd237fcef07f794ea235483afc83eef47a1364`; built from pinned sources by the workflow "AI Smt toolchain" ([`toolchain/`](../OutWit.Controller.AI.Smt.Assets/toolchain)), two builds compared byte for byte |
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
4. makes the process's sandbox (the module is compiled at the first package of a process) and
   the degree (below);
5. solves the problems with the pool and writes each row to the package of results as its
   session ends, in the order the sessions end;
6. uploads the rows and returns an `SmtResultPackageData`: the blob, its SHA-256 and size, the
   number of rows and the package's ticket.

One package is solved at a time in a process; the other packages of a share wait with their
blobs downloaded. A problem that fails on its own (a refusal, an error of the solver's, a
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

### The pool and the degree

The sessions of a package run on threads of their own below normal priority, each with a stack
that holds a guest's. The degree is `min(logical cores, 16, sessions the memory holds)`. One
session may take twice the host's cap of the solver's linear memory (the heap of the solver's
exception objects is capped apart from the linear memory and may take as much again) and a
margin of 192 MiB for the host's part of a session (its output, its answers as text, the problem
as the sanitizer took it, the row being written, the guest's stack); the sessions may claim three
quarters of the memory the machine has available as the runtime sees it. Under the default
options a session counts 2,752 MiB: a machine with 4 GiB available runs one, with 16 GiB four,
with 64 GiB sixteen (seventeen fit). A machine where not one session fits refuses the package
(`memory_insufficient`).

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
codes of a node's refusal, the size of a package, the degree rule, the benchmark's unit, the
platforms and the pool as totals. Nothing in either names a machine.

The benchmark of `Smt.SolvePackage` gives the allocator a rate: thirty small problems written for
it (every query; linear integers and reals, bit-vectors, uninterpreted functions, arrays,
datatypes, strings), the same on every node, solved by the pool at the node's degree as a
package is, the set given often enough that each session gets about four problems a run. A
warm-up run, then timed runs; the rate is problems a second of the median run, in the unit
`smt-problem@ref-v1`. Every problem is held to its outcome and its fuel, the same on every
machine, so a node that computes otherwise cannot score; a node without a sandbox or without
memory for one session reports the default (unranked) score.

## Licence

MIT. The module carries one assembly that is not the controller's own, the wasmtime binding:
see `THIRD-PARTY-NOTICES.txt`. The solver module, Z3, is MIT and brings its licence and its
notices in its own archive.
