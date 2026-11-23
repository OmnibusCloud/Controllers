# OutWit.Controller.AI.Smt

SMT problems solved on anonymous nodes by a pinned solver, with every answer reproducible: Z3
runs as a WebAssembly module inside the controller's own wasm sandbox, under budgets that are
counts and not clocks, so the same problem gives the same verdict, the same model and the same
cost on every machine.

**Status: 0.1.0, in development; nothing is published.** This version holds the solver itself:
the profile, the sandbox and one session per problem. It registers no activity and no variable
yet, ships no script, and its solver module is not a data asset yet: the module is looked for
under `wasm/` in the controller's folder, and a node without it says so. The language layer (the
reader, the sanitizer, the model evaluator, the problem set and the options) is
[`OutWit.Controller.AI.Smt.Model`](../OutWit.Controller.AI.Smt.Model/README.md).

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

## Licence

MIT. The module carries one assembly that is not the controller's own, the wasmtime binding:
see `THIRD-PARTY-NOTICES.txt`. The solver module, Z3, is MIT and brings its licence and its
notices in its own archive.
