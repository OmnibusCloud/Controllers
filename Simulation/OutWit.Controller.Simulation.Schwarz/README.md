# OutWit.Controller.Simulation.Schwarz

Distributed solver for stationary (elliptic) problems — steady heat conduction
and Poisson-class fields — via overlapping **Restricted Additive Schwarz**
domain decomposition. Subdomains are solved across the pool every round,
boundary strips are exchanged through the server, convergence control runs
server-side. Designed for on-premise LAN pools (frequent small rounds).

**Status: v0.1 — algorithm complete and gate-tested, not yet published.**
Distributed runs reproduce the in-memory reference **bitwise** and match the
single-machine solver to ~1e-13 relative; a node failure mid-wave is absorbed
by reassignment with a bitwise-identical result.

## Job script

```
Blob:field = SchwarzSolve(model, opts);
```

`SchwarzSolve.wit` (bundled, ships via `OutWit.Controller.Simulation.Schwarz.Scripts`):
decompose → rounds of `Grid.ForEach` subdomain solves with a relative-residual
stop → final emit wave → assembled Field blob (OWSM kind=4).

The model is an OWSM Model blob (kind=1) built with
`SimulationModelDefinition` from `OutWit.Controller.Simulation.Model`:
box grid, per-node or constant conductivity and source, Dirichlet/Neumann
faces.

## Activities

| Activity | Runs | Purpose |
|---|---|---|
| `Schwarz.Decompose` | server | model → subdomain blobs + `SchwarzPlan` (validates feasibility) |
| `Schwarz.InitRound` | server | round-0 `SchwarzRound` state |
| `Schwarz.RoundBudget` | server | `SchwarzOptions` → Int for `Loop(...)` |
| `Schwarz.MakeTasks` / `Schwarz.MakeFinalTasks` | server | per-subdomain `SchwarzTask` collection (final wave emits owned fields) |
| `Schwarz.SolveSubdomain` | node | pure per-task solve: factorize once, back-substitute per round; boundary package out + lagged residual |
| `Schwarz.Advance` | server | fixed-order residual reduction, boundary routing, round++ |
| `Schwarz.IsConverged` | server | relative-residual stop check → Bool |
| `Schwarz.Assemble` | server | stitch owned field slices → result Field blob |

## Options (`SchwarzOptions`)

| Option | Default | Notes |
|---|---|---|
| `Parts` | 0 (auto = 4) | subdomain count |
| `Overlap` | 3 | cells; **minimum 2** (the convergence measure needs an interior halo) |
| `Eps` | 1e-8 | relative residual stop |
| `MaxRounds` | 60 | budget; unconverged jobs still assemble the best field |
| `Coarse` | false | **not available in v1** — requesting it fails loudly (see honest limits) |
| `ThreadsPerNode` | 0 (all) | reserved for the solver backend |

## Honest limits (v1)

- **One-level iteration**: round counts grow with the part count (~1/H —
  measured 37/60/78 rounds for 2/4/8 parts on the reference problem). The
  two-level coarse correction needs the true global residual, which the
  pure-node/strips-only transport cannot produce; it arrives with the v1.1
  server-held-iterate variant. `Coarse=true` fails loudly rather than run
  degraded.
- **Structured box grids only** (5/7-point FD); unstructured meshes arrive with
  the v2 external-solver seam.
- **Sizing**: hard feasibility cap 2M cells per extended subdomain (RAM bound).
  The practical sweet spot is **≤64³ per subdomain**: the single-threaded
  sparse Cholesky factorization was measured at ~5 min for 64³ and scales
  ~quadratically — it runs once per subdomain per node (cached), after which
  every round is a cheap back-substitution. Larger subdomains are legal but
  budget the setup time. The v2 solver backend is the capacity lever.
- At least one Dirichlet face is required (pure-Neumann is singular in v1).
- More overlap converges faster (measured 101/60/42 rounds for overlap 2/3/4)
  at the cost of larger halos and boundary blobs.

## Benchmark

`Schwarz.SolveSubdomain` benchmarks itself as the activity in miniature: one
Cholesky factorization + 20 back-substitutions of a deterministic in-code 40³
reference (unit `subdomain-solve@40^3-v1`), so node scores rank real solve
throughput.

Design source: `@Simulation/schwarz-controller-deep-dive.md` as amended by
`@Simulation/simulation-controllers-revision-1-code-reality.md` (repo-local docs).
