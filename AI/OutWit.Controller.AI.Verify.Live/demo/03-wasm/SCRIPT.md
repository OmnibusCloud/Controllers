# 03 · Raw wasm: programs a lab compiles itself

The shape of running code that a lab builds with its own compilers: the network gets a
WebAssembly module, not a source, and runs it against the tests. Any language that targets
`wasm32-wasip1` works (C, C++, Rust, Zig, TinyGo, …). The module's bytes travel with the task,
and the sandbox compiles each module once per batch.

## The problems

Three problems, each solved in C and in Rust, and each with the mistakes compiled programs
usually make:

| Problem | Mistakes |
|---|---|
| sum of the integers on stdin (64-bit) | an `int` total that wraps past 2^31; a Rust `parse::<i32>().unwrap()` that panics on a value past i32 |
| number of primes up to N (argument) | trial division by every smaller number (the budget runs out at N = 1,000,000); a range that leaves N itself out |
| most frequent word, case-insensitive | the case kept as written |

Two more modules ask for what the sandbox does not offer, and are refused before they run:
- one imports a host function (`env.connect_host`);
- one is built for threads (`wasm32-wasip1-threads`: a shared, imported memory).

Every task has a budget of 5 G fuel.

## Files

- `src/`: the sources, the way a lab writes them.
- `make_taskset.py <modules>` writes `taskset.jsonl` (each task carries its module, base64) and
  `candidates.csv` (problem, candidate, language, the verdict it gets, why) from the compiled
  modules.
  - The modules come from the controller's toolchain (`toolchain/build.sh`, `kit-*.wasm`): the
    pinned wasi-sdk 34 and Rust 1.85.0.
  - The build server rebuilds them, runs `make_taskset.py` on them, and compares the taskset
    and the table with the committed ones, byte for byte.
- `table.py` prints the verdict of every module from a run's report and checks each against
  `candidates.csv`.

## Run

From `AI/OutWit.Controller.AI.Verify.Live`, with a `wit_sk_` key in `WITAI_VERIFY_LIVE_KEY`:

```
dotnet build -c Release
bin/Release/net10.0/verify-live run demo/03-wasm/taskset.jsonl --runs 2 --batch-size 2 --sampling 1 --save demo-out/wasm
python demo/03-wasm/table.py demo-out/wasm/report-1.json
```

`--sampling 1` runs every package on a second machine as well, and the integrity line counts
the pairs that agree. A module compiled once runs the same on Windows, Linux and macOS.

## What to look at

- The correct solutions pass in both languages.
- Each mistake gets the verdict of its kind:
  - the wrapped total and the missing limit: `Fail`;
  - the panic on the third case: `RuntimeError`;
  - the naive count: `Timeout`.
- The network import and the threads module: `CompileError` before anything runs, with the
  reason on stderr.
- `verdicts the kit does not expect: 0`.
- The report's hash is the same in both runs and on any fleet; `EXPECTED.md` has the measured one.
