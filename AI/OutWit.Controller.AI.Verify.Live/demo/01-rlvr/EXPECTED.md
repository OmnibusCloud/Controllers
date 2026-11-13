# 01 · RLVR: the expected result

Measured on a six-node test fleet (Windows x64 and Linux x64) with Verify 0.8.0, the commands of
`SCRIPT.md` as they stand.

## The rewards table

```
problem            reward     py-correct  py-empty-input       py-float     js-correct     rb-correct    lua-correct      js-concat
p01-sum            4/7              pass          crash           fail           pass           pass           pass           fail
p02-reverse-words  4/7              pass           fail           fail           pass           pass           pass           fail
p03-vowels         4/7              pass           fail           fail           pass           pass           pass          crash
p04-fibonacci      4/7              pass        timeout           fail           pass           pass           pass           fail
p05-prime          4/7              pass        timeout           fail           pass           pass           pass           fail
p06-gcd            4/7              pass           fail        timeout           pass           pass           pass          crash
p07-brackets       4/7              pass           fail          crash           pass           pass           pass           fail
p08-max-subarray   5/7              pass           fail           pass           pass           pass           pass           fail
p09-top-word       4/7              pass           fail           fail           pass           pass           pass           fail
p10-binary         4/7              pass           fail           fail           pass           pass           pass           fail

rewards earned: 41 of 70
  lua-5.4.8         10 of 10
  python-3.14.6     11 of 30
  quickjs-0.15.1    10 of 20
  ruby-3.4.1        10 of 10
verdicts the kit does not expect: 0
```

## The run

| | Run 1 | Run 2 |
|---|---|---|
| Verdicts (70 tasks) | Pass 41, Fail 22, RuntimeError 4, Timeout 3 | the same |
| `report-N.json` | 15,555 bytes, SHA-256 `facaa9c804f602da7372b0a215976a9a2977d0782adff3f54fc3c116f398fab6` | the same bytes |
| `outputs-N.jsonl` | SHA-256 `5f7685769ea222649f3cffcb5f2e00a0f96cd7c4a68bd161de019c3c0da22c22` | the same bytes |
| Integrity | 35 packages run twice, 35 compared, 0 mismatches | the same |
| Wall time | 23 s | 21 s |

## What may differ on another fleet

- The wall time, which packages the sampling picks, and the provenance (which platforms ran
  what, at which degree). None of them is in the report or the outputs.
- The report and the outputs are the same bytes on any fleet. They change only when a verdict,
  an exit code or a program's output changes, that is, when a runtime module changes; the
  controller's release notes name every such change.
