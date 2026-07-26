# OutWit.Controller.Simulation.Parareal

Distributed solver for **transient** (parabolic) problems — heat conduction and
diffusion — via **parareal** parallel-in-time integration. The time interval is
cut into slabs; each slab is propagated across the pool with a fine
Crank–Nicolson integrator while a cheap coarse propagator runs serially on the
server and drives the correction. Designed for crowd/WAN pools, where few rounds
with chunky transfers beat many small ones.

**Version 0.1.6** (Model core 0.1.5,
`OutWit.Controller.Simulation.Parareal.Scripts` 0.1.1).

**Status: v0.1 — algorithm complete and gate-tested, not yet published to a
public feed.** Distributed runs reproduce the in-memory reference **bitwise**;
the exact-slab property (after k iterations, slabs 0..k−1 equal the serial fine
integration exactly) holds to the last bit; a second run on warm caches is
bitwise identical to the first; a node failure mid-wave is absorbed by
reassignment with a bitwise-identical result.

## Job script

```
Blob:timeline = PararealSolve(model, opts);
```

`PararealSolve.wit` (bundled, ships via
`OutWit.Controller.Simulation.Parareal.Scripts`): slice → iterations of
`Grid.ForEach` slab propagations with server-side correction and a relative
correction-norm stop → final snapshot wave (outputs are recomputed once from the
converged states) → Timeline blob (OWSM kind = 6).

```
Job:PararealSolve(Blob:model, PararealOptions:opts)
{
    PararealPlan:plan = Parareal.Slice(model, opts);
    PararealState:state = Parareal.Init(model, plan);
    Int:maxIter = Parareal.IterationBudget(opts);
    PararealTaskCollection:tasks;
    PararealResultCollection:wave;
    Bool:converged = false;

    Loop(maxIter)
    {
        tasks = Parareal.MakeTasks(plan, state);
        wave = Grid.ForEach(task in tasks) => Parareal.Propagate(task);
        state = Parareal.Correct(plan, state, wave);
        converged = Parareal.IsConverged(state);
        If(converged) { Break; }
    }

    tasks = Parareal.MakeSnapshotTasks(plan, state);
    wave = Grid.ForEach(task in tasks) => Parareal.Propagate(task);
    Blob:timeline = Parareal.Collect(plan, wave, state);
    Return(timeline);
}
```

The input is the same OWSM Model blob (kind = 1) the Schwarz controller takes,
built with `SimulationModelDefinition` from
`OutWit.Controller.Simulation.Model` — plus the initial condition
(`InitialConstant` / `InitialPerNode`) and, optionally, a source time curve.

## Supported physics envelope

Linear transient heat conduction / diffusion: the θ-method semi-discretization
`M u' = b − A u` of `−∇·(k∇u) = f`, where `M` is the control-volume measure.
There is no separate volumetric heat capacity — the transient form runs at
`ρc = 1`, so a physical capacity has to be folded into the caller's time scale.
For stationary problems use `OutWit.Controller.Simulation.Schwarz`.

- **Geometry**: structured node-centered box grids, **Cartesian** or
  **axisymmetric r–z**. An axis with a single node is inactive, so 3D, 2D and 1D
  models all go through the same path. Axisymmetric models are 2D in (r, z) with
  `OriginX` = inner radius ≥ 0 and axis 2 inactive; volumes and face areas carry
  the r-weight, and the `r = 0` axis is a zero-area face — a flux or film
  condition there is naturally inert (the symmetry axis is adiabatic).
- **Boundary conditions**, one per box face: **Dirichlet** (`u = g`),
  **Neumann** (outward co-normal flux `g = k ∂u/∂n`), **Robin / film**
  (`h·(T∞ − u)`, with `Value` = T∞ and `Coefficient` = h > 0).
- **Pinned nodes** (`SimulationNodePin`): exact point or partial-face Dirichlet
  at individual nodes. The fine propagator carries them exactly; the coarse
  propagator snaps them to the nearest coarse node, which is harmless because
  G only drives a correction.
- **Void nodes**: nodes outside the body — no equation, no value, adiabatic
  toward material — so a voxelized shape with holes solves on its bounding box.
  Coarse voids are taken by the same injection sampling the coefficient fields
  use.
- **Fields**: conductivity, source and the initial field are constant or
  per-node, in the same lexicographic layout as the solution. A per-node initial
  field is the natural way to restart from a previous solution.
- **Time-varying sources**: `f(x)·a(t)`, where the field carries the shape and
  `SimulationTimePoint` rows carry `a(t)` — linearly interpolated between rows,
  **clamped** (never extrapolated) outside the range, times strictly ascending.
  Only the right-hand side changes along the solve, so the one-time
  factorization stays valid, and the θ-scheme samples the factor at **both ends
  of each step** (`θ·a(t+Δt) + (1−θ)·a(t)`).

### Solvability rule

A **transient** solve needs **no pinning at all**: the time derivative
regularizes the operator, because the stepper's matrix `M + θΔt·A` is symmetric
positive definite regardless of boundary pinning. A pure-Neumann (fully
adiabatic) transient model is legal here, unlike in a stationary solve.

What is still required:

1. Both faces of every **active** axis must be declared. (Faces of an inactive
   single-node axis need nothing.)
2. A Robin face needs `Coefficient > 0`; conductivity must be positive on the
   body; per-node arrays must be exactly `NodeCount` long; a pin must lie inside
   the grid, outside every void, and must not contradict a Dirichlet face value.
3. Source curve times must be strictly ascending.
4. Axisymmetric models need axis 2 inactive, an active radial axis, and
   `OriginX ≥ 0`.

### Explicit non-goals

- **Nonlinearity lives outside the controller.** Radiation, temperature-dependent
  conductivity and any other outer iteration are resolved by the **caller** as a
  sequence of linear solves: update the coefficient/source fields from the last
  answer, submit the next linear job. The controller stays linear by design —
  that is what keeps the operator SPD and every propagator on the Cholesky path.
- No contact.
- No cavity radiation, no view factors.
- No coupled fields.
- Structured box grids only; unstructured meshes are not supported.
- **Diffusive physics only.** Parareal converges poorly on wave-dominated
  problems; those are out of scope by design.

## Activities

| Activity | Runs | Purpose |
|---|---|---|
| `Parareal.Slice` | server | model + options → `PararealPlan`; validates the model, time parameters, coarsening divisibility and the timeline budget, and pins the last slab boundary exactly to `TotalTime` |
| `Parareal.Init` | server | serial coarse sweep from u₀ → round-0 slab states |
| `Parareal.IterationBudget` | server | `PararealOptions` → Int for `Loop(...)` |
| `Parareal.MakeTasks` | server | per-slab `PararealTask` collection for the next wave, frontier-aware (converged slabs leave the wave) |
| `Parareal.MakeSnapshotTasks` | server | the final wave — all slabs, snapshots on |
| `Parareal.Propagate` | node | pure per-slab Crank–Nicolson propagation; factorize once, cached |
| `Parareal.Correct` | server | serial coarse sweep + parareal correction `F + (G_new − G_old)`, frontier advance, round++ |
| `Parareal.IsConverged` | server | relative correction-norm stop check → Bool |
| `Parareal.Collect` | server | merge snapshot packs (OWSM kind = 7) → Timeline blob with the convergence record |

## Options (`PararealOptions` / `PararealOptionsData`)

| Option | Default | Meaning |
|---|---|---|
| `Slabs` | `0` (auto = 4) | time slab count, ideally about the usable pool size |
| `Eps` | `1e-6` | relative correction-norm stop |
| `MaxIterations` | `10` | iteration budget K |
| `Coarsening` | `0` (auto) | spatial coarsening factor of the coarse propagator; auto takes 2 when `(n−1)` is even on every active axis and falls back to 1 (G on the fine grid) otherwise |
| `SliceByBenchmark` | `false` | **RESERVED, not consumed in v1** — benchmark-proportional slab sizing |
| `TotalTime` | `1` | simulated horizon T |
| `FineStepsPerSlab` | `10` | Crank–Nicolson steps per slab; `δt = TotalTime / (Slabs · FineStepsPerSlab)` |
| `SnapshotsPerSlab` | `1` | output snapshots per slab (1 = slab-end states only) |

Validation applied before any propagation is scheduled
(`PararealOptionsValidator`, plus the kernel build):

- `MaxIterations` must be **at least 1**.
- **`Eps` must be strictly positive.** A non-positive tolerance can never be met
  and would silently burn the whole budget on every run, so it is a named
  rejection.
- `SnapshotsPerSlab` must be at least 1 — and in the distributed path, at most
  `FineStepsPerSlab` (you cannot emit more snapshots than there are fine steps).
- **Timeline budget**: `Slabs × SnapshotsPerSlab × NodeCount × 8` bytes must not
  exceed `MAX_TIMELINE_BYTES` = 1 GiB. The check runs before any compute is
  spent, and the message names the offending product.
- `TotalTime`, `Slabs` and `FineStepsPerSlab` must be positive.
- A non-zero `Coarsening` request is **verified, never silently lowered**: it
  must divide `(n−1)` on every active axis.
- The full spatial grid must stay within `SimulationLimits.MAX_LOCAL_DOF`.

## Limits

- **Efficiency is bounded by the iteration count K.** Wall-clock speedup is
  about `N/K`, and K is set by the propagator mismatch, not by N — a regression
  test asserts that K stays flat as the slab count grows (K at N = 12 is at most
  K at N = 6 plus one, and strictly below the trivial N-iteration sweep), so the
  speedup grows with the pool. Expect 20–50 % parallel efficiency, not N×.
- **Diffusive physics only** — see non-goals.
- **Parareal scales time, not memory.** Space never splits, so the **full
  spatial grid** must fit on one node: the hard cap
  `SimulationLimits.MAX_LOCAL_DOF = 2 000 000` is checked against the whole grid
  while the kernel is built, when the answer is still actionable, rather than
  discovered as an out-of-memory kill on a compute node. The practical sweet
  spot is **≤ 64³**: the one-time Crank–Nicolson factorization was measured at
  roughly 5 minutes for 64³ single-threaded and scales about quadratically. It
  is cached per node for the whole job, after which every time step is a cheap
  back-substitution.
- **The coarse propagator runs 4 implicit-Euler sub-steps per slab**
  (`PararealKernel.COARSE_STEPS_PER_SLAB`) on the coarsened grid. This is a
  measured amendment to the textbook one-step design: a literal single step per
  slab was too coarse (propagator error ~0.2 at λΔt ≈ 1.6, contraction only
  ~0.3 per iteration, K ≥ N), while a few sub-steps stay asymptotically free
  next to F and restore the expected K ≈ 3–6 regime.
- **Spatial coarsening costs a couple of extra iterations on small grids**
  (regression-tested at no more than +3); the penalty shrinks with `h²` at
  production sizes.
- **Determinism** is a contract, not a coincidence: all reductions are
  fixed-order (`FixedOrderNorms`), so a convergence test cannot flip on
  reduction order across machines, thread counts or runs.

### Convergence record

The Timeline blob carries an optional `SimulationConvergenceInfo` section:
`Converged` (was the stopping criterion met inside the budget), `Iterations`
(parareal corrections completed), `Eps` (the tolerance used), `Scale` (the
largest absolute value seen in the round-0 coarse sweep, which anchors the
relative criterion) and `History` (the max successive-correction norm after each
iteration). An **absent** record means the producer recorded nothing — never
read it as "did not converge".

## Running the algorithm in one process

`PararealInMemorySolver.Solve` is the same iteration the script drives, without
blob transport — the reference the distributed runs are compared against bitwise,
and the shortest path to understanding the algorithm.
`PararealInMemorySolver.SolveSerialFine` is the exactness oracle beside it.

```csharp
using OutWit.Controller.Simulation.Model;
using OutWit.Controller.Simulation.Model.Parareal;

var model = new SimulationModelDefinition
{
    Nx = 33, Ny = 33, Nz = 1,
    Hx = 1.0 / 32, Hy = 1.0 / 32,
    ConductivityConstant = 1,
    SourceConstant = 1,
    InitialConstant = 0
};

foreach (var face in new[] { SimulationFace.XMin, SimulationFace.XMax, SimulationFace.YMin, SimulationFace.YMax })
    model.Boundaries.Add(new SimulationBoundaryCondition(face, SimulationBcKind.Dirichlet, 0));

// Optional: f(x) * a(t). The source ramps up over the first half of the run,
// then holds (the curve is clamped, never extrapolated).
model.SourceCurve.Add(new SimulationTimePoint(0.0, 0.0));
model.SourceCurve.Add(new SimulationTimePoint(0.5, 1.0));

var options = new PararealOptionsData
{
    Slabs = 6,
    Eps = 1e-6,
    MaxIterations = 10,
    Coarsening = 0,          // auto: 2 here, since (n-1) = 32 is even on both active axes
    TotalTime = 1,
    FineStepsPerSlab = 10,
    SnapshotsPerSlab = 1
};

PararealSolveReport report = PararealInMemorySolver.Solve(model, options);

// report.SlabStates         — Slabs + 1 full-grid fields at the slab boundaries;
//                             the last one is the final-time solution
// report.Iterations         — iterations run; after k of them the first k slabs
//                             match the serial fine integration exactly
// report.Converged          — did the correction norm reach Eps x scale
// report.CorrectionHistory  — max successive-correction norm after each iteration

double[][] serial = PararealInMemorySolver.SolveSerialFine(model, options);   // the oracle
```

## Benchmark

`Parareal.Propagate` benchmarks itself as the activity in miniature: one
Crank–Nicolson factorization plus **20 CN steps** of a deterministic in-code
**40³** reference (unit `slab-step@40^3-v1`). 40³ rather than 64³ for the same
measured reason as the Schwarz benchmark — the 64³ factorization takes about
5 minutes single-threaded, far past the 5–15 s target, while 40³ (64 000
unknowns) lands in the window and still stays far out of L3. The unit string is
deliberately distinct from the Schwarz one: a subdomain solve and a slab step
are different work and must never be ranked on one scale. The result carries the
grid size, the unknown count and a checksum of the final field, so two nodes'
runs are verifiable as the same computation and not merely the same duration.

## Version compatibility

The controller and `OutWit.Controller.Simulation.Parareal.Scripts` ship the
`.wit` script **separately** and version independently. The script calls
activities by name and the engine resolves them when the script is seeded, so
the compatibility surface is the activity names and their parameter lists — not
the assembly's binary API.

A controller upgrade that changes an activity signature therefore needs the
matching Scripts package version on the server. A mismatch surfaces at seed time
or job start as a script compilation failure — "activity not found", or an arity
error of the form `Expected 2 parameter(s), got 3` — not as a wrong answer.
Adding an activity is forward-compatible; changing one is not. Upgrade both
sides together.

## Dependencies

- `Variables` 1.0.0+ — `Blob`, `Int`, `Bool`
- `OutWit.Controller.Simulation.Model` 0.1.5 — shared types, blob formats and
  the numerical core (see its README for the OWSM format and the model API)

The bundled script drives its waves with `Grid.ForEach`, so the Grid controller
has to be available on the server that runs the job.
