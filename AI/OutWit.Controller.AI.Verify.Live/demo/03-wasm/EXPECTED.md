# 03 · Raw wasm: the expected result

Measured on a six-node test fleet (Windows x64 and Linux x64) with Verify 0.11.3, using the
commands of `SCRIPT.md`.

## The table

```
problem  candidate     language verdict       why
sum      c             C        Pass          64-bit total
sum      rust          Rust     Pass          64-bit total
sum      c-int-total   C        Fail          an int total wraps past 2^31
sum      rust-unwrap   Rust     RuntimeError  parses as i32 and unwraps: a panic
primes   c             C        Pass          a sieve
primes   rust          Rust     Pass          a sieve
primes   c-naive       C        Timeout       trial division by every smaller number
primes   rust-below    Rust     Fail          leaves the limit itself out
words    rust          Rust     Pass          case-insensitive
words    c             C        Pass          case-insensitive
words    c-case        C        Fail          keeps the case as written
edges    c-network     C        CompileError  imports a host function the sandbox does not provide
edges    c-threads     C        CompileError  a shared memory: threads are not offered
verdicts the kit does not expect: 0
```

## The run

| | Run 1 | Run 2 |
|---|---|---|
| Verdicts (13 modules) | Pass 6, Fail 3, CompileError 2, RuntimeError 1, Timeout 1 | the same |
| `report-N.json` | 2,484 bytes, SHA-256 `8c123aa817126c779b1ddbd6c219f5d4b55f4da3813fd285a9424edf248b3901` | the same bytes |
| `outputs-N.jsonl` | SHA-256 `1e984dcffd1f7adc8aeac667f9e23fa25717557dcb52378ce8187f70cfb8a0bb` | the same bytes |
| Integrity | 13 packages run twice (`--sampling 1`), 13 compared, 0 mismatches | the same |
| Wall time | 2.2 s | 2.2 s |

The two refused modules show why in their stderr:
- the network one: `module failed to instantiate: unknown import: env::connect_host has not been defined`;
- the threads one: the module fails to compile, because shared memory is not offered.

## What may differ on another fleet

- **Can differ:** the wall time, and which machines ran the two copies of each package.
- **Does not differ:** the report and the outputs are the same bytes on any fleet. The modules are compiled once by the lab (here by the controller's toolchain), so the bytes the network runs never change between runs.
