# 02 · Math: a synthetic corpus, generated and checked

The shape of building verified training data for mathematics. Generator programs make problems
with their answers. A checker verifies every answer by another route, and only the verified
items go into the corpus. Here the generators and the checker are Python with sympy, and they
run on the network as two fan-outs of one job.

There are six families of problems: a derivative, an antiderivative, the roots of a polynomial,
a simplification, a closed-form sum, a limit. Each family has six generator runs, with the
problem drawn from the run's seed. Two of the six are flawed the way a hurried generator is:

| Family | Flaw |
|---|---|
| derivative | the chain rule forgotten |
| integral | one term's sign flipped |
| roots | a root lost |
| simplify | the constant dropped |
| sum | the bound off by one |
| limit | a factor missed |

Two more generators fail on purpose:
- one writes prose instead of the format;
- one names a family no generator knows.

The checker does not repeat the generator's work. It checks the answer from the other side:
- an antiderivative by differentiating it;
- roots by substituting them and counting them against the degree;
- a closed-form sum on concrete values of `n`;
- a limit by computing it.

It reads one JSON line from stdin and prints `OK` or `MISMATCH …`, exiting 0 either way, so a
crash of the checker itself would stay distinguishable.

## Files

- `make_taskset.py` writes the kit and the table; run it again and the files do not change.
  - `generators.jsonl`: 38 generator tasks, one source with arguments family, seed and flaw.
  - `checker.json`: the checker spec.
  - `candidates.csv`: per generator, the verdict of the generation and of its check.
- `corpus.py` reads a saved run, prints per family what was generated, verified and rejected,
  writes the verified items (`verified-1.jsonl`), and checks every verdict against
  `candidates.csv`.

## Run

From `AI/OutWit.Controller.AI.Verify.Live`, with a `wit_sk_` key in `WITAI_VERIFY_LIVE_KEY`:

```
dotnet build -c Release
bin/Release/net10.0/verify-live check demo/02-math/generators.jsonl demo/02-math/checker.json --runs 2 --batch-size 4 --sampling 0.5 --save demo-out/math
python demo/02-math/corpus.py demo-out/math 1
```

`check` runs `VerifyGenerateAndCheck`. The generators fan out over the network. Every
generator that printed something becomes one run of the checker, with that output on stdin and
under the generator's index. The checks fan out in turn.

`--runs 2` runs the whole job twice: the two reports, and the two corpora, are the same bytes.
`--sampling 0.5` runs half the check packages on a second machine as well.

## What to look at

- Per family: 4 of 6 generated items verified and 2 rejected.
- The prose and the unknown family: the prose is rejected by the checker as malformed; the
  unknown family fails at generation and is never checked.
- `verdicts the kit does not expect: 0`.
- Duplicates. The verified corpus repeats a few problems: the families with few parameters (the
  sum has three powers) draw the same problem from different seeds. A generator at scale
  repeats itself, and a corpus pipeline deduplicates after checking.
- The report and the corpus are the same bytes in both runs and on any fleet; `EXPECTED.md`
  has the measured hashes.
