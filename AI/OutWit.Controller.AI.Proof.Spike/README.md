# Proof controller: the PF-0 gate (a spike)

Measurements that come before a controller which checks Lean 4 proofs against a pinned Mathlib:
does the same proof get the same verdict and the same heartbeat count on every platform, does
one compiled environment replay everywhere, can a proof accepted on one machine be confirmed on
another without running its code, what does a checking process cost, and how does the
environment get to a machine. Nothing here ships; the folder is scripts, pins and one list.

Everything runs on GitHub-hosted runners (`.github/workflows/ai-proof-pf0.yml`). No Lean file,
no Mathlib file, no dataset text and no binary is committed.

| Path | What |
|---|---|
| `pins.env` | every download: Lean, Mathlib and its dependencies, the REPL, landrun, the dataset file; version, commit, URL, SHA-256 |
| `lean/PF0.lean` | the gate's own measuring commands (heartbeats of one command; kind, statement, axioms and term hash of a declaration) |
| `harness/pf0lib.py`, `machine.py` | what the scripts share (pins, hashing, a process runner that reports time and memory on three systems, the assembly of an item); what the runner is |
| `harness/toolchain.py`, `mathlib.py`, `tools.py` | the official Lean release, the environment by Mathlib's cache tool, the REPL and landrun; each step timed and sized |
| `harness/startup.py`, `smoke.py` | what a process costs before it checks anything (the import alone); the measuring prelude compiled and the smoke sets |
| `harness/check.py` | the fresh-process path: one `lean` process per item |
| `harness/pool.py`, `degree.py` | the pool path: long-lived REPL processes; memory as processes are added |
| `harness/replay.py` | `leanchecker` over a slice of the environment |
| `harness/split.py` | check and export on one machine (`leanexport`), confirm on another (`lake comparator` on two exports) |
| `harness/sandbox.py` | landrun on Linux: a refused write, a refused connection, the same verdicts inside |
| `harness/pack.py` | delivery options of the environment: the cache's files unpacked by the toolchain's `leantar`, plain zstd, smaller environments |
| `harness/baseline_kimina.py` | a baseline on request: the Kimina Lean Server on one runner with the same items |
| `golden/items.py`, `selection.py`, `fetch.py`, `list.tsv` | the golden set: chosen once from NuminaMath-LEAN (Apache-2.0), committed as a list of ids and hashes, rebuilt from the pinned file in every run |
| `golden/smoke-*.jsonl` | a few items written for the gate, one or more per verdict class |
| `report/report.py` | the platforms side by side |

An item has the form the controller's `check` mode takes: a trusted challenge (a theorem with
`sorry` as its proof) and an untrusted solution (the same declaration with a proof). The
verdicts: `accepted`, `rejected`, `incomplete`, `axioms_refused`, `statement_mismatch`, `budget`,
`timeout`, `memout`, and `challenge_error` when the challenge itself no longer elaborates.

Heartbeats are read inside one command: `#pf0_measure "s" in <declaration>` takes
`IO.getNumHeartbeats` before and after elaborating the declaration on the same thread, the
counter Lean's own `maxHeartbeats` budget reads. A process is started with
`-DElab.async=false`, so the declaration (its proof and the kernel's check of it) is elaborated
on that one thread.
