# OutWit.Controller.Simulation.Parareal.Scripts

Content-only NuGet package that ships the bundled `.wit` job script for the
`OutWit.Controller.Simulation.Parareal` controller. There is no managed
assembly in this package — just the script text plus a consumer-side MSBuild
`.targets` file that stages it into the layout the OmnibusCloud runtime reads
at startup.

## Contents

| Script | Job signature | Returns |
|---|---|---|
| `PararealSolve.wit` | `PararealSolve(Blob:model, PararealOptions:opts)` | `Blob` — the assembled Timeline (OWSM kind=6) |

One script, one job. Everything else in the package is packaging.

## What the script does

`PararealSolve.wit` drives a parareal parallel-in-time integration: the horizon
is cut into time slabs that the pool propagates with a fine Crank-Nicolson
integrator, while a cheap coarse propagator and the correction step run
serially on the server.

1. **Slice.** `Parareal.Slice(model, opts)` cuts the horizon into time slabs,
   resolves the coarse-grid factor and returns a `PararealPlan`, failing loudly
   if the requested slicing is not feasible.
2. **Init.** `Parareal.Init(model, plan)` runs the serial coarse sweep from the
   initial condition to produce the round-0 slab start states;
   `Parareal.IterationBudget(opts)` turns the options into the `Int` bound for
   the loop.
3. **Iterations.** Inside `Loop(maxIter)`:
   - `Parareal.MakeTasks(plan, state)` builds the `PararealTask` collection for
     this round — frontier-aware, so slabs already exact drop out of the wave;
   - `Grid.ForEach(task in tasks) => Parareal.Propagate(task)` fans the wave out
     across the pool, one slab per task, each node factorizing its
     Crank-Nicolson operator once and caching it for the rest of the job;
   - `Parareal.Correct(plan, state, wave)` folds the wave server-side: serial
     coarse sweep, parareal correction, exact-frontier advance, round++;
   - `Parareal.IsConverged(state)` checks the relative correction-norm stop and
     `If(converged) { Break; }` leaves the loop early.
4. **Deferred snapshot wave.** `Parareal.MakeSnapshotTasks(plan, state)` plus
   one more `Grid.ForEach` of `Parareal.Propagate` recomputes every slab from
   its converged start state with snapshot output switched on. Intermediate
   snapshots therefore never cross the wire during the iteration phase — output
   volume is paid once, at the end.
5. **Collect.** `Parareal.Collect(plan, wave, state)` merges the snapshot packs
   into the result Timeline blob, which the job returns.

An unconverged job still collects and returns the timeline it reached; the
convergence flag and the iteration count travel in the result metadata.

## Calling it

The script declares the job, so a caller supplies only the two inputs:

- `model` — a `Blob` handle for an OWSM Model blob (kind=1), the same format
  the stationary solver consumes, plus the initial condition; typically built
  through `SimulationModelDefinition` from `OutWit.Math.Simulation`;
- `opts` — a `PararealOptions` variable carrying the tuning set (`Slabs`,
  `Eps`, `MaxIterations`, `Coarsening`, `TotalTime`, `FineStepsPerSlab`,
  `SnapshotsPerSlab`). It is a job input, not something a script constructs.

In a hand-written script the same call reads:

```
Blob:timeline = PararealSolve(model, opts);
```

The script compiles against three controllers besides Parareal itself:
`Variables` for its value types, `Special` for the loop's control flow
(`Loop`, `If`, `Break`, `Return`) and `Grid` for the `Grid.ForEach` fan-out.
`Special` and `Grid` are host-only, so they must be present **on the server**
that compiles and runs the job — they are not manifest dependencies of the
Parareal module, which also has to load on compute nodes that never receive
them.

## Where the files land

Inside the nupkg:

```
content/scripts/PararealSolve.wit
build/OutWit.Controller.Simulation.Parareal.Scripts.targets
lib/net10.0/_._                     # framework-compatibility marker only
```

The `build/` targets auto-imports into any csproj that references the package.
Its `OutWitCopyPararealScripts` target runs before `Build` and copies
`content/scripts/*.wit` flat into the consumer's output:

```
bin/Debug/net10.0/
  @Scripts/
    PararealSolve.wit
```

That flat `@Scripts/` folder is exactly what the server's script seeder reads
on startup. Copies are incremental (`SkipUnchangedFiles`), so a rebuild that
did not change the script text does not touch the staged file.

## Versioning and controller pairing

Current versions: this package **0.1.1**, controller
`OutWit.Controller.Simulation.Parareal` **0.1.6**, shared
numerical core `OutWit.Math.Simulation` **0.2.0**. Both Simulation
controllers and their Scripts packages are published to the OmnibusCloud
organization feed rather than nuget.org; the numerical core comes from the
separate private WitMath feed.

**The script ships separately from the controller, and that is deliberate.**
A `.wit` script resolves activities by name at runtime, through the engine
compiler — there is no managed reference from this content-only package to the
controller DLL, and the two version independently on their own SemVer lines.
The compatibility surface is the activity names and their parameter
signatures, not the controller's binary API. See
`docs/adr-001-script-controller-version-compatibility.md` for the full policy.

The operational consequence is worth stating plainly: **upgrading the
Parareal controller across a change to an activity's parameter list requires
deploying the matching Scripts version alongside it.** A mismatch is not
silent and does not produce a wrong answer — the script fails to compile when
the job starts, with a parameter-count (arity) or type-mismatch error naming
the offending activity, and the job never runs. The fix is always to deploy
the Scripts version paired with that controller version, not to edit the
staged script in place.

Adding an activity is forward-compatible; renaming one, or changing its
parameter list, is not.

## License

MIT — see the `LICENSE` file at the repository root.
