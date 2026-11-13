# OutWit.Controller.AI.Verify

Sandboxed, deterministic, resource-metered execution of untrusted programs
against specifications — on anonymous volunteer nodes. One primitive
(`Verify.ExecutePackage`) carries several products: code-vs-tests verification
(RLVR-style reward checking), code-corpus generation with checking, math
problem generation with CAS checking, and formal proof checking — generation
and verification are the same activity with different programs.

**Status: 0.9.0 — proven on the test fleet, not yet released to nuget.org.**
`VerifyRun` returns a byte-identical verdict report for a taskset across runs,
placements and fleets: a million small tasks gave the same report on six nodes
(22m41s) and on four (41m35s). Every node executes batches at a bounded degree
below normal priority, reports the delivery and the progress of its share, and
attaches its provenance; `VerifyGenerateAndCheck` runs the generate-then-check
shape end to end.

Isolation targets WASM (wasmtime): deterministic, cross-platform
(Windows / Linux / macOS, x64 / arm64), no admin rights or Docker required on
contributor machines, no ambient authority — network and filesystem do not
exist inside the sandbox unless explicitly virtualized in. Verdicts are
verifiable by construction: code passes its test suite or it does not; a
proof checks or it does not.

## Activities

| Activity | Where | Role |
|---|---|---|
| `Verify.Split` | host | taskset (JSON lines) + options → batches, grouped by runtime, limits clamped to the host ceilings; the batches are grouped into packages, each stored as a blob, and what the activity returns is the references |
| `Verify.ExecutePackage` | node | the primitive: the node downloads the package blob and runs its batches — one compiled runtime module per batch, the tasks executed concurrently in isolated stores under fuel, memory and wall-clock limits — then uploads the results as a blob; progress of the delivery and of the work; batch provenance |
| `Verify.Execute` | node | one task given inline, for diagnostics; a taskset never travels this way |
| `Verify.Collect` | host | result packages → the verdict report (aggregates + one record per task; the run's identity, byte-identical regardless of placement) |
| `Verify.CollectOutputs` | host | result packages → the outputs (one JSON line per task: stdout, stderr, and the outcome and stdout of every case of a suite; byte-deterministic). The sandbox log of a run, the corpus of a generation |
| `Verify.MakeCheckTasks` | host | generator results + a checker spec → check task packages, one check per passing candidate under the candidate's index |
| `Verify.Provenance` | host | result packages → the provenance document (which tasks ran together, on which platform, with which wasmtime and runtime module, at which degree, and what each task cost: fuel, peak memory, wall time; never which machine) |
| `Verify.SamplingRate` | host | options → `SamplingRate` clamped to 0..1, the share of the packages `Grid.Replicas` runs twice |
| `Verify.Integrity` | host | the result packages of all executions → the integrity document (the two executions of each task that ran twice, agreement on verdict, exit, outputs and cases; the mismatch rate) |
| `Verify.Preflight` | host | counts, runtimes referenced, unknown runtimes, size estimate, ceiling notes — before anything runs |
| `Verify.RuntimeDiagnostics` | host | wasmtime version (the native library is loaded to prove it), runtime target, per-runtime availability and hash — of the server's module; a node's module is exercised by `Verify.ExecutePackage` and described by the provenance |
| `Verify.Capabilities` | host | what a submitter can know before a run, as a document: the controller's version, the runtimes it carries, the defaults and ceilings, and the pool as totals (nodes, concurrent sandboxes, reference tasks per second; no node named) |

## Scripts

`OutWit.Controller.AI.Verify.Scripts` bundles:

| Script | Shape | Returns |
|---|---|---|
| `VerifyRun(Blob taskset, VerifyOptions opts)` | Split → ForEach ⇒ ExecutePackage (a share of the packages twice) → Collect, CollectOutputs, Provenance, Integrity | `report`, `outputs`, `provenance`, `integrity` |
| `VerifyGenerate(Blob taskset, VerifyOptions opts)` | the same fan-out; the programs are generators | `corpus`, `report`, `provenance`, `integrity` |
| `VerifyGenerateAndCheck(Blob generators, Blob checker, VerifyOptions opts)` | generators fan out → MakeCheckTasks → checks fan out (a share of the check packages twice) → Collect | `report` (the checks), `corpus` (the candidates), `provenance`, `integrity` |
| `VerifyPreflight(Blob taskset, VerifyOptions opts)` | one host activity | `result` |
| `VerifyRuntimeDiagnostics()` | two host activities | `result`, `capabilities` |

`VerifyOptions`: `DefaultLimits`, `BatchSize` (tasks per batch, 0 = 32),
`MaxDegree` (concurrent sandboxes per node, 0 = 16), `SamplingRate` (the share
of packages executed twice for integrity, 0–1; 0 = none), `ReportFormat` (reserved).

## How tasks and results travel

Nothing that grows with the taskset travels inside a message. `Verify.Split`
groups the batches into packages and stores each package as a blob; the
collection `Grid.ForEach` places on nodes is a list of references (blob id,
hash, size, task count). A node is told which packages are its share, downloads
exactly those over the blob channel — in chunks, resumed after a break — and
returns each package's results as a blob of its own; the host reads the result
blobs when it writes the documents. The message that assigns a node its share
of a million tasks is a few hundred references.

A package is one batch while a run is small, so the split between nodes stays
as fine as the batches. A large run is cut into about a thousand packages
(at most 4,096 tasks or 8 MB of task text each), which keeps the number of
transfers bounded while the shares stay proportional to the nodes' rates. A
package blob is Brotli-compressed; a node checks its hash before it reads the
tasks, and the host checks a result blob's hash before it reads the results.

Measured on the test fleet: the million small tasks of the scale run leave the
host as 1,116 package blobs, 3 MB in all (the tasks are short and alike; real
tasks shrink less). In a 100,000-task run a node whose link from the server
was limited to 8 kbit/s received its 77 packages and finished its share before
the run's last node did.

## Documents — authoring without the SDK

Every input and output is a JSON document with a published schema in
[`docs/`](docs/), so a pipeline in any language can author tasksets and read
results; the SDK only moves blobs and submits scripts.

| Document | Schema | Direction |
|---|---|---|
| taskset (JSON lines, one task per line) | [`verify-taskset.schema.json`](docs/verify-taskset.schema.json) | in |
| checker spec (one object) | [`verify-checker.schema.json`](docs/verify-checker.schema.json) | in |
| verdict report | [`verify-report.schema.json`](docs/verify-report.schema.json) | out |
| outputs of a run, corpus of a generation (JSON lines) | [`verify-corpus.schema.json`](docs/verify-corpus.schema.json) | out |
| capabilities | [`verify-capabilities.schema.json`](docs/verify-capabilities.schema.json) | out |
| provenance | [`verify-provenance.schema.json`](docs/verify-provenance.schema.json) | out |
| integrity | [`verify-integrity.schema.json`](docs/verify-integrity.schema.json) | out |

A minimal taskset — one code-vs-tests task and one plain run:

```json
{"index":0,"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"a=int(input());b=int(input());print(a+b)"},"suite":[{"stdin":"2\n3\n","expected_stdout":"5\n","expected_exit":0}]}
{"index":1,"runtime":"quickjs-0.15.1","entry":"m.js","sources":{"m.js":"console.log(6*7)"}}
```

Verdicts: `Pass`, `Fail` (ran, wrong answer or missing signal), `CompileError`,
`RuntimeError` (crashed, or exited non-zero where 0 was expected; a crash the program did
not explain is named on stderr in fixed words, `wasm trap: …`), `Timeout` (fuel or wall
clock; nothing is written, since one node may stop on fuel where another stops on the
clock), `MemoryExceeded` (the program's allocator was refused), `OutputExceeded`,
`RuntimeUnavailable`.
A suite case passes when the run completes with its `expected_exit` (whatever
it is) and, if given, its exact `expected_stdout`.

Generate-and-check: the generators are an ordinary taskset whose outputs are
the candidates; the checker spec is one task line plus `expected_stdout` /
`expected_exit`. Every candidate that passed generation with output becomes
one run of the checker with the candidate on stdin, under the candidate's
index, so the check report lines up with the generators; candidates that
produced nothing usable are skipped (and still appear in the corpus). The
recommended checker contract: print the verdict and exit 0, so that a crash
of the checker itself stays distinguishable as a `RuntimeError`.

## Integrity

Anonymous nodes are trusted by checking, not by assumption. `SamplingRate` runs
a share of the packages twice, in the same `Grid.ForEach` call as the rest. The
platform picks the packages at random and keeps the two executions on different
machines, so a node cannot tell whether its package is being checked. The
integrity document pairs the two executions of each task and says whether they
agree on verdict, exit code, outputs and per-case outcomes —
exactly the fields an honest execution reproduces anywhere. With a single machine
available nothing runs twice. The mismatch rate is over the compared pairs. The
report and the corpus have one record per task, chosen by rule: the lowest
verdict, exit code, then outputs.

## Determinism

The verdict report is the run's identity: it carries verdicts, exit codes and
per-case outcomes — the fields two honest executions agree on wherever they
ran — and is byte-identical for a taskset regardless of which nodes ran which
packages (proven on the test fleet across three placements). Fuel is
reproducible per host but drifts between hosts for the interpreters that read
files: CPython tasks by 0.0002–0.06 % with the bytecode layers, Ruby and Lua by
a fixed step between Linux and the other two systems (a Lua hello costs 569,680
units on Linux and 602,449 on Windows and macOS) — the difference is in how
each host's WASI answers the interpreter's file and descriptor queries — while
QuickJS tasks and raw WASI modules are byte-equal everywhere; and the peak
linear memory of a CPython task can differ by one 64 KiB page with it; wall
time is hardware. All three are metrics and live per task in the provenance
document. The clock is the run's own (it starts at a pinned instant and moves
only when the program sleeps) and the random source is seeded from the task,
so a program sees the same world on every node; a folder cannot be
listed, so the host's order of a listing is out of sight. What is not pinned
is the metadata of the runtime's own files: their times and file numbers are
the host's, so a program that prints them disagrees between nodes.

## What a program can reach

Every run is one WebAssembly instance in a store of its own, created for the
run and dropped after it. What the store gives a program, and what happens at
each edge:

| | What a program gets | At the edge |
|---|---|---|
| Processor | fuel, one unit an instruction: 50e9 by default, 2e12 at most | `Timeout` |
| Wall time | 10 s by default, 120 s at most | `Timeout` |
| Memory | one linear memory of 32-bit addresses: 256 MiB by default, 2 GiB at most | the allocator refuses inside the program: `MemoryExceeded`; an access outside the memory is a crash: `RuntimeError` |
| Call stack | 2 MiB of the engine's native stack (the default is 512 KiB), on threads made to hold it | `RuntimeError`, `wasm trap: call stack exhausted`; the interpreters stop a deep recursion themselves before that |
| Tables | 1,048,576 elements a table, eight tables | growth is refused |
| Output | 256 KiB of stdout and 64 KiB of stderr by default | `OutputExceeded` |
| Files | the runtime's own library, read-only; for Lua the task's own sources, read-only; a raw `wasi` module and a C/C++ program have none | writing, truncating, removing and renaming are refused |
| Folder listing | none | "permission denied" |
| Network, processes, threads | none | the calls do not exist or fail |
| Clock | the run's own: it starts at one pinned instant and moves only when the program waits | nothing of the host's time is visible |
| Sleeping | returns at once and moves the run's clock by what was asked | an hour's sleep costs nothing, holds no worker, and the clock reads an hour later |
| Randomness | a stream seeded by the task | the same bytes on every node |
| Environment | empty, but for what pins the interpreter | |

A module is refused before it runs (`CompileError`) when it asks for what the
sandbox does not offer: a second linear memory, 64-bit memories or tables,
shared memory, the GC heap, stack switching, exception handling, an import
the sandbox does not provide. Four system calls answer with an error and do
nothing, each for a stated reason (the comments in `VerifyDeterministicLinker`
name the engine advisories): renumbering a descriptor, listing a folder, and
setting a file's times by descriptor or by path.

These are tests, not intentions: the adversarial suite and the raw-WASI suite
run a hostile program or module against every row. The same in plain words,
for someone who lends a computer to the network: [docs/safety.md](docs/safety.md).

## Node behaviour

A worker is often a desktop. A batch runs on dedicated threads below normal
priority (Windows honours it; Linux ignores thread priorities, where the
degree cap is the protection) at `min(cores, usable RAM / per-task memory cap,
MaxDegree)` — 16 by default. Every result batch carries its provenance: runtime
target, wasmtime and native library origin, runtime module hash, degree, task
count, wall time. What a run needs on disk — the files a task's input and
output are redirected to, deleted when the run ends — lives in the client's
own temp folder, and the task packages in the client's blob cache: nothing of
a task is written anywhere else on the machine.

A node starts every package of its share together. The package blobs download
ahead of the work, four at a time, so the share is on the node's disk early and
the rest of the run needs the link only for progress and results; one package
executes at a time, since a batch already takes the node's cores. A download or
an upload that fails is tried again for about five minutes before the package
fails, on top of the blob channel's own retries. The first transfer of a node
goes alone and the rest wait for it (the channel is connected by its first
user), and retries are spread at random so that transfers that failed together
do not come back together.

A run is split once and sent once: each node gets a share by its benchmark
rate, a copy of a sampled package goes to another machine, and nothing is
handed out again while the run goes well. So a run ends when its slowest share
ends. A node that is slower than its benchmark said, or busy with its owner's
work, holds the run until it finishes; a node that goes silent loses its share
after the silence window (90 s by default), and the whole share is sent to
another node and done again from the start. With a benchmark that represents
the work and machines that are really free, the difference is small; on a
fleet of desktops it is the price of sending everything once.

The node reports its share, not one batch: first the delivery (`receiving
tasks: 12/215 packages, 3.1/14.8 MB; 0/220160 tasks done`), then the work
(`51200/220160 tasks`), about twice a second through the host's activity
progress sink. The fraction is tasks done out of the tasks of the node's share.

The node benchmark (`verify-task@mixed-v2`) is the throughput of a fixed
reference batch — one CPU-bound Python program and one QuickJS program, four
per worker thread, with their expected outputs — as tasks per second of the
median run at the node's degree; a node that computes wrong cannot score, a
node without runtimes reports the default (unranked) score. `Grid.ForEach`
allocates packages with the engine's `Balanced` strategy (pre-partitioned by
rate), which the package work estimate (task count) matches.

## Runtimes

Language runtimes are WebAssembly modules, platform-independent and pinned by
SHA-256 in the controller's catalog. A task names its runtime; a batch shares
one compiled module.

| Runtime id | What runs | Entry |
|---|---|---|
| `python-3.14.6` | CPython 3.14.6 (WASI build), pure Python with the standard library | the source, inline |
| `python-3.14.6-sympy-1.14.0` | the same interpreter with sympy 1.14.0 and mpmath 1.4.1 on `PYTHONPATH` | the source, inline |
| `python-3.14.6-numpy-2.5.4` | CPython 3.14.6 with numpy 2.5.4 built in, both built for WASI by this repository's toolchain and started from a snapshot with numpy imported; numpy's default integer is 32-bit (`dtype=np.int64` for 64-bit sums), no BLAS, threads or SIMD | the source, inline |
| `quickjs-0.15.1` | QuickJS-NG 0.15.1, JavaScript, built for WASI by this repository's toolchain | the source, inline |
| `typescript-sucrase-3.35.1` | TypeScript on the same QuickJS: sucrase 3.35.1 strips the types, the JavaScript runs as a module; no type check | the source, inline |
| `ruby-3.4.1` | Ruby 3.4.1 (the ruby.wasm 2.10.1 build) with the standard library and the bundled gems base64, csv, matrix and prime | the source, inline |
| `lua-5.4.8` | Lua 5.4.8, the stock interpreter with its standard library, built for WASI by this repository's toolchain | the sources as files; the entry is the script |
| `clang-22.1.0` | C17 and C++20 without exceptions, compiled and linked on the node by clang 22.1.0 for WASI with wasi-libc and libc++; the program runs as a raw WASI module | the sources as files; every `.c`, `.cc`, `.cpp`, `.cxx` is compiled, headers sit beside them; the entry names the program |
| `wasi` | the task's own WASI command module: Rust, C/C++, Zig, TinyGo, anything that targets wasm32-wasi (`_start`, WASI preview 1) | the module's bytes, base64; its name is `argv[0]` |

The Python library code is bytecode layers, one zip each, that CPython's
zipimport loads straight from `sys.path`: `python314.zip` (the standard
library) and `sympy-1.14.0.zip` (sympy + mpmath). Each layer is compiled by the
pinned interpreter itself as unchecked-hash `.pyc` (no source parsing, no
source stat at import) and pinned by SHA-256 in the catalog next to the
module, so every node runs byte-identical library code and a hello costs a
quarter less fuel than with a source tree, a stdlib import set a tenth. The
prefix is pinned (`PYTHONHOME=/`), nothing lists a directory at start-up.
Library frames show no source line in tracebacks; the sandbox compares
outputs, not tracebacks. sympy's `external/gmpy.py` is patched before
compiling (the C long size comes from a constant: the WASI build has no
`_ctypes`); the patch is declared in the layer's NOTICE.

numpy (since 0.11.0) is a runtime of its own, `python-3.14.6-numpy-2.5.4`: the same CPython
3.14.6 and numpy 2.5.4, both built from pinned sources by the asset tool's toolchain
(`toolchain/python-numpy`: CPython's own WASI driver with wasi-sdk 24; numpy's extension
modules linked into the interpreter as builtins, since WASI loads no shared library; the
standard library and numpy's Python package as bytecode layers compiled by the module itself;
two builds give the same bytes). The module is a start-up snapshot: the interpreter initialized
and numpy imported, written into the module by wizer of the pinned wasmtime 48.0.8 at build
time, so a task starts where a plain start would be after its imports. The sandbox runs it as
any module (`_start`). What a task sees is its own: `sys.argv`, the clock at the sandbox's
pinned start, and every source of randomness that drew entropy while the snapshot was made
(Python's `random`, numpy's legacy global `RandomState`, `tempfile`'s names) seeded again from
the task's stream, so unseeded random numbers follow the task's seed as in `python-3.14.6` (the
numbers differ from a plain start's, the rule does not). The snapshot holds `os.environ`,
`sys.path` and the directories of its two layers, so the module and the layers are pinned and
delivered together (their own data asset, `verify-python-numpy.zip`, next to `wasm/`), and a
run under another environment or file system is refused (exit 2, the reason on stderr). The
module before its snapshot travels beside it as `python-plain.wasm`, for debugging; the catalog
names the snapshot. A hello costs 37 M fuel (`python-3.14.6`: 124 M), `import numpy` nothing more
(a plain start: 0.8 G), a 300×300 `linalg.solve` 0.25 G, a sort of a million integers 0.57 G.

What a numpy author should know about the target, also in the capabilities document's `notes`.
It is 32-bit: numpy's default integer is `int32`, so `np.arange(1_000_000).sum()` wraps to
1783293664; pass `dtype=np.int64` where a total can pass 2^31. There is no BLAS (numpy's own
`lapack_lite`, correct and slower), no threads (`threading` cannot start one) and no SIMD.
`np.longdouble` is IEEE binary128 in software, not x86's 80-bit extended, so long-double
results differ from a desktop's. Floating-point arithmetic sets no flags in WebAssembly: `1.0 /
0` is `inf` without a warning, while numpy's own errors (a singular matrix's `LinAlgError`, an
integer division by zero under `errstate`) are raised. A C++ exception inside numpy would stop the
program (`RuntimeError`); numpy checks its arguments before its C++ code could throw. Nothing is
writable: `np.save` and `np.load` work on `io.BytesIO`. `numpy.testing` is there, numpy's own test
suite is not.

`OutWit.Controller.AI.Verify.Assets` (`verify-assets build <dir>`) rebuilds
the whole set from pinned downloads, byte for byte, and prints the hashes the
catalog pins.

Ruby loads its library from files, not from one archive, so the library is a
tree: 1,142 files, cut unchanged out of the release archive (the standard
library and the `lib` folders of four bundled gems; headers, the static
library and the other gems stay out). The catalog pins the tree with one hash
over the relative path, the length and the bytes of every file, and a node
verifies it before the first task: a file changed, added or removed makes the
runtime unavailable there. The tree is the guest's whole file system, read
only — a program can read nothing that is not under the pin. A program runs
inline without RubyGems (it would scan the file system at every start, and
there is nothing to install); the four gems' folders are on the load path, so
`require 'prime'` works like any other. Not in this build: `bigdecimal`,
sockets, processes (`system` and backticks do nothing). It is a 32-bit build:
an Integer above 2^30 is a bignum, so arithmetic on large totals costs several
times more than on a desktop Ruby. A hello costs 154 M fuel and takes 55 MiB.

QuickJS-NG is built here too (since 0.8.0), from its pinned sources with one patch
(`toolchain/quickjs-wasi.patch`). The project's own WASI build places a 64 KiB C stack right
after its data and switches the engine's stack limit off on WASI, so a deep recursion wrote
over the interpreter's data: a correct program 162 levels deep ended with exit 0 and no output,
and an endless one got a verdict that depended on what the sandbox had run before it. Our build
puts the C stack (1 MiB) below the data, where an overflow traps at address 0, and turns the
limit back on at 128 KiB: a recursion too deep is JavaScript's `RangeError`, which a program can
catch, at about 300 levels of plain recursion and about 100 through callbacks such as `map`,
before the engine's own native stack runs out, on every platform the build server runs. A
structure nested far deeper than that in native code (`JSON.stringify` of 2,000 levels) still
reaches the native stack: the run traps, `RuntimeError`. QuickJS's own tests are a gate of the
toolchain job (the five that fail are about Atomics without threads, os functions, workers and
the `qjs` global, as with the project's build), and the build is the same bytes on any host.

TypeScript runs on the JavaScript runtime's module (one compile serves both) with a read-only
root that holds sucrase's transform, bundled by the toolchain from the npm packages its lock
file pins (`toolchain/typescript`: esbuild 0.25.10, 206 KB, the licences of the seven bundled
packages beside it; the build server rebuilds it and compares the bytes). QuickJS loads it
first (`-I /sucrase.js`); a small bootstrap module holding the task's source as a JSON string
strips the types and evaluates the JavaScript as a module, so `import * as std from 'qjs:std'`,
top-level `await`, `scriptArgs`, stdin and the exit code behave as in a JavaScript task. Enums,
`const enum`, parameter properties, abstract classes, `#private` fields, `satisfies`, `as
const` and generics work; a runtime `namespace` does not (sucrase does not emit one) and there
is no type check. A source that does not parse is a `RuntimeError` whose stderr names the line
and column. A hello costs 0.33 G fuel, most of it loading the transpiler.

Lua is built here from lua.org's sources, unchanged, by the asset tool's toolchain
(`toolchain/build.sh`: wasi-sdk 34 and Binaryen 133, every download pinned, two builds
give the same bytes; the build server checks that the committed `modules/lua-5.4.8/lua.wasm`
is what the sources build to). Lua raises an error by a jump to the nearest protected call,
which WebAssembly can only do with its exception handling, and the sandbox keeps that off
for every module. So the jump is an asyncify unwind that stops at the protected call and
is never rewound (`toolchain/lua-asyncify.h`): the module imports WASI alone and holds no
exception instruction. Lua's own test suite passes as it does with exceptions (27 files of
30; three fail for reasons of WASI: no locale data, no temporary files, no `PATH`), and the
deepest unwind stays under half its 256 KiB buffer; both are gates of the toolchain job.
The cost is about 1.4 times the fuel of a build with exceptions (coroutines are faster);
a hello costs 0.6 M fuel and takes 1.3 MiB.
A Lua task's sources are written as files into a fresh folder in the node's working folder,
which is the guest's whole file system, read-only, and is removed when the task ends; the
entry runs as Lua's script, so `arg`, `...` and `require` of a neighbour or of `lib/x.lua`
work. A name must be the same file on every node: `/` between folders, no `.` or `..`, none
of the characters or names Windows refuses, no two names that differ only in case; anything
else is the task's `CompileError`.

C and C++ are compiled on the node (since 0.10.0) by clang 22.1.0 for WASI: LLVM as one
WebAssembly module (YoWASP's build, 75 MB) and the sysroot it reads at `/usr` (wasi-libc,
libc++, libc++abi, the compiler-rt builtins, clang's headers; 2,345 files pinned as a tree,
like Ruby's library), both cut unchanged from the pinned npm package. WASI starts no
processes, so the build is the clang driver's plan run step by step, each step its own
instance of the module: `clang -cc1` for every `.c` (C17) and every `.cc`, `.cpp`, `.cxx`
(C++20), then `wasm-ld`. Nothing is writable: a step writes its product to stdout (the
object, then the program module), the sandbox puts each object into a fresh read-only
folder, and the next step reads it there. The command lines are fixed in the controller
(`-O2`), the sources are written as files as for Lua (the same name rules; headers sit
beside the sources), and all the steps of a task share one budget of their own, apart from
the program's limits: 100 G fuel (fuel binds, so the same sources build the same way on
every node; a 600 s wall backstop and 1 GiB of memory a step). A C hello builds in 0.2 G
fuel; a C++ program of a couple of hundred lines with the standard library in 10–20 G, about
5 s. The program then runs as a raw WASI module under the task's limits, `argv[0]` the
entry's name without its extension, with no file system at all. A compile or link error is
the task's `CompileError` with clang's diagnostics on stderr. C++ is C++ without exceptions:
the shipped libc++abi has no unwinder and the sandbox keeps exception handling off, so `try`
and `throw` are a compile error. The compiler's module is compiled once per process (about
15 s); the builds and the programs are reproducible to the byte.

What a C or C++ author should know about the target. It is 32-bit: `int`, `long` and
pointers are 32 bits, `long long` is 64, so `long` arithmetic that fits on a desktop may
overflow here. The program's stack is 8 MiB, as on Linux (a local array of a million ints
fits), placed below the data, so an overflow leaves the memory and traps instead of writing
over the globals. A recursion also uses the engine's call stack, 2 MiB: a small recursive
function reaches about 25,000 to 40,000 levels (the frame decides, and a little the
platform), so a depth-first walk of a 100,000-node chain must be written with an explicit
stack. Crashes are `RuntimeError` with the trap named on stderr when the program said nothing
(`wasm trap: out of bounds memory access` for a wild pointer or an overflowed stack,
`wasm trap: unreachable code reached` for `abort` and a failed `assert`), and `new` past the
memory cap is `MemoryExceeded` (libc++ says `bad_alloc` and aborts). A write through a null
pointer does not crash: address 0 is memory like any other. `-O2` removes what it can prove
unused, an allocation included.

The raw `wasi` runtime has no module of its own: each task carries one,
compiled by the lab offline, and the sandbox compiles it on the node (cached by
content hash within a batch). args, stdin, suite and limits work as for any
task; a module that does not validate or cannot be instantiated (an import the
sandbox does not provide) is a `CompileError`, one without `_start` a
`RuntimeError`. A 224-byte hello, assembled by hand in the tests:

```json
{"runtime":"wasi","entry":"hello.wasm","sources":{"hello.wasm":"AGFzbQEAAAABEANgBH9/f38Bf2ABfwBgAAACZwMWd2FzaV9zbmFwc2hvdF9wcmV2aWV3MQhmZF93cml0ZQAAFndhc2lfc25hcHNob3RfcHJldmlldzEHZmRfcmVhZAAAFndhc2lfc25hcHNob3RfcHJldmlldzEJcHJvY19leGl0AAEDAgECBQMBAAEHEwIGbWVtb3J5AgAGX3N0YXJ0AAMKDwENAEEBQQBBAUEIEAAaCwssAQBBAAsmIAAAAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABoZWxsbwo="},"suite":[{"expected_stdout":"hello\n"}]}
```

Honest scope: pure Python with the standard library, Python with numpy, JavaScript, TypeScript
(types stripped), Ruby with its standard library, Lua, C and C++ (no exceptions) built on the
node, and compiled WASI modules. No C extension inside CPython-WASI but numpy's, built in; no
gems beyond the four named.

## What the module carries

This is the first controller that calls a native library in-process (wasmtime,
through the `Wasmtime.Dotnet` binding), so the deployed module carries the
library per platform in the .NET layout its `deps.json` describes:

```
verify.module/
  OutWit.Controller.AI.Verify.dll, .deps.json, controller.json
  OutWit.Controller.AI.Verify.Model.dll
  Wasmtime.Dotnet.dll                      staged by the build
  runtimes/<rid>/native/                   wasmtime, one data asset per RID
  wasm/                                    the language runtime modules, one data asset
  wasm/python-3.14.6-numpy-2.5.4/          the numpy runtime, a data asset of its own
```

Two load modes meet this layout. An isolated plugin load context (the server,
the module-load test) resolves the library through the module's `deps.json`.
The cloud client loads controllers into the shared load context, where that
file is never read and a P/Invoke probes only the application folder and the
module root — so the controller registers `VerifyNativeLibrary` as the
binding's DllImport resolver before its first wasmtime call, and it loads
`runtimes/<rid>/native/` from the module itself (the host's runtime
identifier first, then the portable one).

The natives and the runtime modules are Tier-2 data assets (`ControllerDataAsset`),
fetched by consumers at build time from the `verify-v<version>` release of this
repository and verified by hash; the nupkg itself stays small. The engine is the
long-term line 48 of wasmtime: the .NET binding 48.0.2 (the `Wasmtime` package) over
the native library of its newest patch, 48.0.8, taken unchanged from the engine's own
release archives (`toolchain/natives.sh` of the asset tool, pinned by the digests the
release states; the build server runs the tests on the same library on each platform).
A patch of the line changes neither an output nor a unit of fuel; a major release would
change fuel, so the fleet stays on one line. Licences travel inside the asset archives
(wasmtime: Apache-2.0 WITH LLVM-exception; CPython: PSF; QuickJS-NG: MIT;
Ruby and its bundled gems: Ruby's own terms or BSD-2-Clause; the ruby.wasm
port: MIT; Lua: MIT; sucrase and the six packages bundled with it: MIT, Apache-2.0 and
others, each named in the licence file beside the bundle; LLVM, clang, lld, libc++ and YoWASP's build: Apache-2.0, LLVM's WITH LLVM-exception;
wasi-libc: Apache-2.0 WITH LLVM-exception, Apache-2.0 or MIT, with cloudlibc's BSD-2-Clause
and musl's MIT inside; numpy: BSD-3-Clause with 0BSD, MIT, Zlib and CC0-1.0 parts, each named in
`LICENSE-numpy.txt`; the numpy build's recipes and two numpy patches adapted from wasi-python:
Apache-2.0, `LICENSE-wasi-python.txt`).

`VerifySelfCheck.Run()` reports, from inside a loaded module, where the binding
and the runtimes resolved from and whether one task per runtime ran — the
module-load test calls it through the real isolated loader, and an operator
can call it as a doctor-style check.

## Tests

`OutWit.Controller.AI.Verify.Tests`: wire-layout guards, the schema guards,
the parser at its edges, the sandbox verdict, adversarial, hygiene, raw-WASI,
Python-layer, numpy, Ruby, Lua, QuickJS, TypeScript and C/C++ suites, the tree hash, the worker pool (cancellation
included) and the
benchmark, the taskset, sampling and integrity pipelines, the package format,
plan, transfer (a link that drops and comes back included) and the share's
progress, the end-to-end runs
of every bundled script through the engine on in-process nodes, and the
module self-containment proof. The sandbox suites need the pinned runtime set
(`verify-assets build OutWit.Controller.AI.Verify.Tests/@Data/runtimes`;
`@Data/` is gitignored) and Assert.Ignore without it; the `AI Verify` workflow
builds the set and runs everything on Linux, macOS and Windows, where a runtime
that does not resolve is a failure, not a skip.

Load tests (`Category=Load`, not in the sweep): a thousand tasks at the
machine's degree, thousands of runs with bounded memory, hundreds of distinct
raw modules against the bounded module cache, a hundred thousand lines
through the parser and the planner and a hundred thousand results through
every writer, and thousands of tasks through `VerifyRun` with sampling on
the in-process nodes — `dotnet test --filter Category=Load`.
