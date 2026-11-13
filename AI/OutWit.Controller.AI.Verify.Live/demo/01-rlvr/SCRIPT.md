# 01 · RLVR: rewards for sampled solutions

The shape of reinforcement learning with verifiable rewards: a model samples several solutions
to each problem, every sample runs against the problem's tests, and the verdict is its reward.
Here ten small problems have seven candidates each, as a model would sample them — correct
solutions in Python, JavaScript, Ruby and Lua, and the mistakes models make: a missed edge case,
the wrong output format, an algorithm that never ends in time, a crash, lost precision. Seventy
tasks, four test cases each, one budget for all (20 G fuel).

## Files

- `make_taskset.py` writes `taskset.jsonl` (the tasks) and `candidates.csv` (index, problem,
  candidate, runtime, the verdict the candidate gets); run it again and the files do not change.
- `rewards.py` turns a run's report into the rewards table and checks every verdict against
  `candidates.csv`.

## Run

From `AI/OutWit.Controller.AI.Verify.Live`, with a `wit_sk_` key in `WITAI_VERIFY_LIVE_KEY`:

```
dotnet build -c Release
bin/Release/net10.0/verify-live run demo/01-rlvr/taskset.jsonl --runs 2 --batch-size 5 --sampling 0.5 --save demo-out/rlvr
python demo/01-rlvr/rewards.py demo-out/rlvr/report-1.json
```

`--runs 2` runs the set twice: the two reports are the same bytes. `--sampling 0.5` sends half the
packages to a second machine as well, and the integrity line counts the pairs that agree.

## What to look at

- The rewards table: each problem earns 4 or 5 of 7, the correct solutions in all four
  languages pass, every mistake gets the verdict its kind deserves (`fail`, `crash`, `timeout`).
- `verdicts the kit does not expect: 0`.
- The report's hash is the same in both runs and on any fleet; `EXPECTED.md` has the measured one.
