# OutWit.Controller.Simulation.Parareal

Distributed solver for transient (parabolic) problems — heat conduction and
diffusion — via **parareal** parallel-in-time integration: time slabs are
propagated across the pool with a fine Crank–Nicolson integrator while a cheap
coarse propagator runs serially on the server. Designed for crowd/WAN pools
(few rounds, chunky transfers).

**Status: v0.1 — algorithm complete and gate-tested, not yet published.**
Distributed runs reproduce the in-memory reference **bitwise**; the exact-slab
property (after k iterations, slabs 0..k−1 equal the serial fine integration
exactly) holds to the last bit; a node failure mid-wave is absorbed by
reassignment with a bitwise-identical result.

## Job script

```
Blob:timeline = PararealSolve(model, opts);
```

`PararealSolve.wit` (bundled, ships via
`OutWit.Controller.Simulation.Parareal.Scripts`): slice → iterations of
`Grid.ForEach` slab propagations with server-side correction and a relative
correction-norm stop → final snapshot wave (outputs are recomputed once
from converged states) → Timeline blob (OWSM kind=6) the bridge writes as a
multi-step result.

The model is the same OWSM Model blob (kind=1) Schwarz uses, plus the initial
condition (`SimulationModelDefinition.Initial*`).

## Activities

| Activity | Runs | Purpose |
|---|---|---|
| `Parareal.Slice` | server | model + options → `PararealPlan` (validates feasibility, resolves coarsening) |
| `Parareal.Init` | server | serial coarse sweep from u₀ → round-0 slab states |
| `Parareal.IterationBudget` | server | `PararealOptions` → Int for `Loop(...)` |
| `Parareal.MakeTasks` / `Parareal.MakeSnapshotTasks` | server | per-slab `PararealTask` collection (frontier-aware; snapshot wave covers all slabs) |
| `Parareal.Propagate` | node | pure per-slab Crank–Nicolson propagation; factorize once, cached |
| `Parareal.Correct` | server | serial coarse sweep + parareal correction, frontier advance, round++ |
| `Parareal.IsConverged` | server | relative correction-norm stop → Bool |
| `Parareal.Collect` | server | merge snapshot packs → Timeline blob |

## Options (`PararealOptions`)

| Option | Default | Notes |
|---|---|---|
| `Slabs` | 0 (auto = 4) | time slab count ≈ usable pool size |
| `Eps` | 1e-6 | relative correction-norm stop |
| `MaxIterations` | 10 | budget K |
| `Coarsening` | 0 (auto: 2 if divisible, else 1) | coarse-grid factor; (n−1) per active axis must divide |
| `TotalTime` | 1 | simulated horizon T |
| `FineStepsPerSlab` | 10 | Crank–Nicolson steps per slab; δt = T/(Slabs·Steps) |
| `SnapshotsPerSlab` | 1 | output snapshots per slab (1 = slab-end states) |

## Honest limits (v1)

- **Efficiency is bounded by the iteration count K**: wall-clock speedup ≈
  N/K with K set by the propagator mismatch, not by N — measured K(N=6)=6,
  K(N=12)=5 on the reference heat problem, i.e. the speedup grows with the
  pool. Expect 20–50 % parallel efficiency, not N×.
- **Diffusive physics only** (heat/diffusion class): parareal converges poorly
  on wave-dominated problems — out of scope in v1 by design.
- **Parareal scales time, not memory**: the full spatial problem must fit one
  node (hard cap 2M cells; practical sweet spot **≤64³** — the one-time
  Crank–Nicolson factorization was measured at ~5 min for 64³ single-threaded
  and scales ~quadratically; it is cached per node for the whole job).
- The coarse propagator runs 4 implicit-Euler sub-steps per slab on the
  coarsened grid (a measured amendment to the original one-step design — one
  step per slab stalled convergence at K≥N).
- Spatial coarsening (factor 2) costs a couple of extra iterations on small
  grids; the penalty shrinks ∝h² at production sizes.

## Benchmark

`Parareal.Propagate` benchmarks itself as the activity in miniature: one
Crank–Nicolson factorization + 20 CN steps of a deterministic in-code 40³
reference (unit `slab-step@40^3-v1`).
