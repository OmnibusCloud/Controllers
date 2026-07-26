# OutWit.Controller.Simulation.Schwarz

Distributed solver for **stationary** (elliptic) problems — steady heat
conduction and Poisson-class fields — via overlapping **Restricted Additive
Schwarz** domain decomposition. The model is cut into overlapping subdomains,
every round solves them across the pool, boundary strips are exchanged through
the server, and convergence control runs server-side. Designed for on-premise
LAN pools, where frequent small rounds are cheap.

**Version 0.1.6** (Model core 0.1.5, `OutWit.Controller.Simulation.Schwarz.Scripts`
0.1.1).

**Status: v0.1 — algorithm complete and gate-tested, not yet published to a
public feed.** Distributed runs reproduce the in-memory reference **bitwise**
and match the single-machine reference solver to within the gated 1e-9 relative;
a second run on warm factorization caches is bitwise identical to the first; a
node failure mid-wave is absorbed by reassignment with a bitwise-identical
result. Gates cover 2D, 3D, Robin faces and mixed Dirichlet/Neumann models
through the real distributed pipeline.

## Job script

```
Blob:field = SchwarzSolve(model, opts);
```

`SchwarzSolve.wit` (bundled, ships via
`OutWit.Controller.Simulation.Schwarz.Scripts`): decompose → rounds of
`Grid.ForEach` subdomain solves with a relative-residual stop → final emit wave →
assembled Field blob (OWSM kind = 4).

```
Job:SchwarzSolve(Blob:model, SchwarzOptions:opts)
{
    SchwarzPlan:plan = Schwarz.Decompose(model, opts);
    SchwarzRound:state = Schwarz.InitRound(plan);
    Int:maxRounds = Schwarz.RoundBudget(opts);
    SchwarzTaskCollection:tasks;
    SchwarzResultCollection:wave;
    Bool:converged = false;

    Loop(maxRounds)
    {
        tasks = Schwarz.MakeTasks(plan, state);
        wave = Grid.ForEach(task in tasks) => Schwarz.SolveSubdomain(task);
        state = Schwarz.Advance(plan, state, wave);
        converged = Schwarz.IsConverged(state);
        If(converged) { Break; }
    }

    tasks = Schwarz.MakeFinalTasks(plan, state);
    wave = Grid.ForEach(task in tasks) => Schwarz.SolveSubdomain(task);
    Blob:field = Schwarz.Assemble(plan, wave, state);
    Return(field);
}
```

The input is an OWSM Model blob (kind = 1) built with
`SimulationModelDefinition` from `OutWit.Math.Simulation`.

## Supported physics envelope

Linear heat conduction / Poisson class: `−∇·(k∇u) = f`, symmetric positive
definite, solved by sparse Cholesky. Stationary only — for transient problems
use `OutWit.Controller.Simulation.Parareal`.

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
  at individual nodes, which whole-face conditions cannot express.
- **Void nodes**: nodes outside the body. They carry no equation and no value,
  and every face they share with a material node is adiabatic — so a voxelized
  shape with holes or recesses solves on its bounding box.
- **Fields**: conductivity and source are constant or per-node, in the same
  lexicographic layout as the solution, so slices paste without reindexing.
- **Source time curves** are a transient feature and are **rejected here** —
  a stationary operator has no time to vary along.
- The initial-condition field of the model is ignored by a stationary solve.

### Solvability rule

Assembly refuses an unsolvable model instead of handing a singular matrix to the
factorization:

1. Both faces of every **active** axis must be declared.
2. The solve must be pinned by at least one of: a **Dirichlet face**, a **Robin
   face** (`h > 0` adds to the diagonal), or a **pinned node**. Pure Neumann is
   singular and is rejected. (This rule is stationary-only; a transient solve
   needs no pinning, because the time derivative regularizes the operator.)
3. When **voids** are present, rule 2 is checked **per connected component of
   material nodes**: a region that voids have cut off from every prescribed
   value, film face and pin is singular on its own and is rejected by name.
4. A Robin face needs `Coefficient > 0`; conductivity must be positive on the
   body; per-node arrays must be exactly `NodeCount` long; a pin must lie inside
   the grid, outside every void, and must not contradict a Dirichlet face value.

### Explicit non-goals

- **Nonlinearity lives outside the controller.** Radiation, temperature-dependent
  conductivity and any other outer iteration are resolved by the **caller** as a
  sequence of linear solves: update the coefficient/source fields from the last
  answer, submit the next linear job. The controller stays linear by design —
  that is what keeps the operator SPD and every solve on the Cholesky path.
- No contact.
- No cavity radiation, no view factors.
- No coupled fields.
- Structured box grids only; unstructured meshes are not supported.

## Activities

| Activity | Runs | Purpose |
|---|---|---|
| `Schwarz.Decompose` | server | model + options → subdomain blobs (OWSM kind = 2) + `SchwarzPlan`; validates feasibility and rejects `Coarse=true` |
| `Schwarz.InitRound` | server | round-0 `SchwarzRound` state |
| `Schwarz.RoundBudget` | server | `SchwarzOptions` → Int for `Loop(...)` |
| `Schwarz.MakeTasks` | server | per-subdomain `SchwarzTask` collection for the next wave |
| `Schwarz.MakeFinalTasks` | server | the final wave — same tasks, but each emits its owned field slice |
| `Schwarz.SolveSubdomain` | node | pure per-task solve: factorize once (cached), back-substitute per round; emits a boundary package and the lagged residual |
| `Schwarz.Advance` | server | fixed-order residual reduction, boundary routing, round++ |
| `Schwarz.IsConverged` | server | relative-residual stop check → Bool |
| `Schwarz.Assemble` | server | stitch owned field slices → result Field blob with the convergence record |

## Options (`SchwarzOptions` / `SchwarzOptionsData`)

| Option | Default | Meaning |
|---|---|---|
| `Parts` | `0` (auto = 4) | subdomain count |
| `Overlap` | `3` | overlap width in cells; **minimum 2** |
| `Eps` | `1e-8` | relative residual stop: the run converges when the residual falls to `Eps × (round-0 residual)` |
| `MaxRounds` | `60` | round budget; an unconverged job still runs the final wave and assembles the best field |
| `Coarse` | `false` | **RESERVED, not available in v1** — `true` fails loudly |
| `ThreadsPerNode` | `0` (all) | **RESERVED, not consumed in v1** — the CSparse solve is single-threaded |

Validation applied before any compute:

- `Parts` must resolve to at least 1, and the chosen split must leave at least
  **2 owned cells** on any axis it actually cuts; a thinner slice is swallowed by
  its own overlap halo.
- `Overlap` must be at least **2**. The lagged residual compares halo layers
  strictly inside the overlap band, so at overlap 1 the band is only the imposed
  interface surface and the residual degenerates to zero.
- Every extended (owned + overlap) subdomain must stay within
  `SimulationLimits.MAX_LOCAL_DOF`.
- `Coarse = true` is rejected with a named error rather than silently running a
  degraded solve.
- The model itself is validated by the solvability rule above.

`Eps` and `MaxRounds` are not separately range-checked: a nonsensical value
simply exhausts the budget and assembles an unconverged field, which the
convergence record then reports honestly.

## Limits

- **One-level iteration.** Round counts grow with the part count (roughly
  `1/H`); a regression test pins that trend for 2 / 4 / 8 parts. The two-level
  coarse correction needs the true global residual of the current iterate, which
  the strips-only transport cannot produce — deflating the only residual
  available (the lagged one) was measured to *slow* convergence. It arrives with
  the server-held-iterate variant, and until then `Coarse=true` fails loudly
  rather than run degraded.
- **More overlap does not converge slower** (regression-tested at overlap 2 / 3 /
  4), at the cost of larger halos and bigger boundary blobs.
- **Structured box grids only** (5/7-point control-volume stencil).
- **Sizing.** `SimulationLimits.MAX_LOCAL_DOF = 2 000 000` is a hard feasibility
  cap on the **extended subdomain node count** — the largest system one machine
  is asked to factorize — and it is checked while the decomposition is being
  built, when the answer is still actionable ("use more parts, or coarsen"),
  rather than discovered as an out-of-memory kill on a compute node. The
  practical sweet spot is **≤ 64³ per subdomain**: the single-threaded sparse
  Cholesky factorization was measured at roughly 5 minutes for 64³ and scales
  about quadratically. It runs once per subdomain per node and is cached, after
  which every round is a cheap back-substitution — so larger subdomains are legal
  up to the cap, but budget the setup time.
- **Determinism** is a contract, not a coincidence: all reductions are
  fixed-order (`FixedOrderNorms`), so a convergence test cannot flip on
  reduction order across machines, thread counts or runs.

### Convergence record

The assembled Field blob carries an optional `SimulationConvergenceInfo`
section: `Converged` (was the stopping criterion met inside the budget),
`Iterations` (rounds completed), `Eps` (the tolerance used), `Scale` (the
round-0 residual that anchored the relative criterion) and `History` (the global
residual after each round). It is written only on the full-field result;
per-subdomain slices carry none. An **absent** record means the producer
recorded nothing — never read it as "did not converge".

## Running the algorithm in one process

`SchwarzInMemorySolver.Solve` is the same iteration the script drives, without
blob transport — the reference the distributed runs are compared against bitwise,
and the shortest path to understanding the algorithm.

```csharp
using OutWit.Math.Simulation;
using OutWit.Math.Simulation.Numerics;
using OutWit.Math.Simulation.Schwarz;

var model = new SimulationModelDefinition
{
    Nx = 65, Ny = 65, Nz = 1,
    Hx = 1.0 / 64, Hy = 1.0 / 64,
    ConductivityConstant = 1,
    SourceConstant = 1
};

foreach (var face in new[] { SimulationFace.XMin, SimulationFace.XMax, SimulationFace.YMin, SimulationFace.YMax })
    model.Boundaries.Add(new SimulationBoundaryCondition(face, SimulationBcKind.Dirichlet, 0));

var options = new SchwarzOptionsData
{
    Parts = 4,
    Overlap = 3,
    Eps = 1e-8,
    MaxRounds = 60
};

SchwarzSolveReport report = SchwarzInMemorySolver.Solve(model, options);

// report.Field            — the stitched global field, lexicographic (x fastest)
// report.Rounds           — rounds completed, excluding the final emit pass
// report.Converged        — did the residual reach Eps x (round-0 residual)
// report.ResidualHistory  — the lagged-residual norm after each round

double[] oracle = SimulationReferenceSolver.Solve(model);   // single-machine direct solve
```

## Benchmark

`Schwarz.SolveSubdomain` benchmarks itself as the activity in miniature: one
Cholesky factorization plus **20 back-substitutions** of a deterministic in-code
**40³** reference (unit `subdomain-solve@40^3-v1`), so node scores rank real
solve throughput rather than raw clock speed. 40³ rather than 64³ because the
64³ factorization measured about 5 minutes single-threaded — roughly 20× past the
5–15 s target — while 40³ (64 000 unknowns) lands in the window and still stays
far out of L3. The result carries the grid size, the unknown count and a
checksum of the solution, so two nodes' runs are verifiable as the same
computation and not merely the same duration.

## Version compatibility

The controller and `OutWit.Controller.Simulation.Schwarz.Scripts` ship the
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
- `OutWit.Math.Simulation` 0.2.0 — shared types, blob formats and the
  numerical core. Built from the private WitMath repository rather than from
  this one, so building this controller needs credentials for that feed.

The bundled script additionally needs two controllers **on the server that
runs the job**: `Special` for the round loop's control flow (`Loop`, `If`,
`Break`, `Return`) and `Grid` for the `Grid.ForEach` fan-out.

They are deliberately **not** declared as manifest dependencies. Both are
host-only controllers that are never delivered to compute nodes, whereas this
module carries host activities (decompose, advance, assemble) and the node
activity (subdomain solve) together. Since the dependency check also runs
node-side, declaring them would make every node refuse to load this module and
the distributed solve would never start. They are a script requirement of the
server, not a dependency of the module.
