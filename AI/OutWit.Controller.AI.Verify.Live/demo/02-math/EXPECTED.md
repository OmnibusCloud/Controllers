# 02 · Math: the expected result

Measured on a six-node test fleet (Windows x64 and Linux x64) with Verify 0.11.3, using the
commands of `SCRIPT.md`.

**With Verify 1.0.0** the verdicts below are unchanged: a test of the controller runs every
row of this kit in the sandbox (`VerifyDemoKitTests`). The report is `verify-report/3`: since the
version named above it gained a `schema` tag and three counters, and states the sampling rate,
whether the sampling took place (`as_asked` on a fleet of two machines or more) and the packages
nothing answered (0), so the sizes and SHA-256 values in the table of the run are those of the
version named above. The fuel figures of the provenance are higher: fuel counts the bytes a
program reads and prints and a thousand units for every call into the host, and a module the
engine compiles on a node pays for its smallest functions. The sizes, the hashes and the fuel
are measured again when the kit is next walked on a fleet.

On a macOS arm64 node, with every task of the kit on two machines and the other copy on a Linux
x64 node (Verify 0.17.0), no pair of copies differed and every verdict was the one below. 1.0.0
adds to that code only the estimate of a compile's memory, which refuses no task of this kit: the
test above runs every row under 1.0.0.

## The corpus

```
family       generated  verified  rejected  failed
derivative           7         4         3       0
integral             6         4         2       0
roots                6         4         2       0
simplify             6         4         2       0
sum                  6         4         2       0
limit                6         4         2       0
matrix               0         0         0       1
total               37        24        13       1
verified items: 24 (18 distinct problems with their answers)
verdicts the kit does not expect: 0
```

The derivative family rejects three items: its two flawed seeds and the generator that writes
prose. The unknown family fails at generation, so it is never checked.

## The run

| | Run 1 | Run 2 |
|---|---|---|
| Check verdicts (37 checks) | Pass 24, Fail 13 | the same |
| `report-N.json` | 3,587 bytes, SHA-256 `3bc628c23d8b8798b067be3bff7ab71317c042ce892ddb46433ed5562f1888a5` | the same bytes |
| `corpus-N.jsonl` (38 lines) | SHA-256 `2b2d455e91ca2a23fda90664ab4b19a33b029c6ea64a50c091a84ba87999c25e` | the same bytes |
| `verified-N.jsonl` (written by `corpus.py`) | SHA-256 `b964e0ff767b0b6e9a05b6525576262ff1628b61886873d22786cb6dcb535a23` | the same bytes |
| Integrity | 17 check packages run twice, 17 compared, 0 mismatches | the same |
| Wall time | 27 s | 29 s |

## What may differ on another fleet

- **Can differ:** the wall time, which packages the sampling picks, and the provenance (which platforms ran what). None of these is in the report or the corpus.
- **Does not differ:** the report, the corpus and the verified items are the same bytes on any fleet. They change only when a runtime module changes, and the controller's release notes name every such change.
