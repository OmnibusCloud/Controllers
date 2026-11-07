# OutWit.Controller.Simulation.Schwarz.Scripts

Content-only NuGet package that ships the bundled `.wit` job script for the
`OutWit.Controller.Simulation.Schwarz` controller. There is no managed
assembly in this package — just the script text plus a consumer-side MSBuild
`.targets` file that stages it into the layout the OmnibusCloud runtime reads
at startup.

## Contents

| Script | Job signature | Returns |
|---|---|---|
| `SchwarzSolve.wit` | `SchwarzSolve(Blob:model, SchwarzOptions:opts)` | `Blob` — the assembled Field (OWSM kind=4) |

One script, one job. Everything else in the package is packaging.

## What the script does

`SchwarzSolve.wit` drives an overlapping Restricted Additive Schwarz solve:
the server owns decomposition, convergence control and assembly, the pool owns
the per-subdomain solves.

1. **Decompose.** `Schwarz.Decompose(model, opts)` splits the model blob into
   overlapping subdomain blobs and returns a `SchwarzPlan`, failing loudly if
   the requested decomposition is not feasible.
2. **Seed.** `Schwarz.InitRound(plan)` produces the round-0 `SchwarzRound`
   state; `Schwarz.RoundBudget(opts)` turns the options into the `Int` bound
   for the loop.
3. **Rounds.** Inside `Loop(maxRounds)`:
   - `Schwarz.MakeTasks(plan, state)` builds one `SchwarzTask` per subdomain;
   - `Grid.ForEach(task in tasks) => Schwarz.SolveSubdomain(task)` fans the
     wave out across the pool — each node factorizes its subdomain once and
     back-substitutes on every later round, returning a boundary package and a
     lagged residual rather than the field itself;
   - `Schwarz.Advance(plan, state, wave)` folds the wave server-side in a fixed
     order (deterministic residual reduction), routes boundary strips to their
     neighbours and increments the round;
   - `Schwarz.IsConverged(state)` checks the relative-residual stop and
     `If(converged) { Break; }` leaves the loop early.
4. **Final emit wave.** `Schwarz.MakeFinalTasks(plan, state)` plus one more
   `Grid.ForEach` of `Schwarz.SolveSubdomain` re-runs back-substitution from the
   factorizations already cached on each node, this time uploading the owned
   field slices. Bulk field data therefore crosses the wire once, at the end,
   instead of every round.
5. **Assemble.** `Schwarz.Assemble(plan, wave, state)` stitches the owned slices
   into the result Field blob, which the job returns.

An unconverged job still assembles and returns the best field it reached; the
convergence flag and the round count travel in the result metadata.

## Calling it

The script declares the job, so a caller supplies only the two inputs:

- `model` — a `Blob` handle for an OWSM Model blob (kind=1), typically built
  through `SimulationModelDefinition` from `OutWit.Math.Simulation.Model`;
- `opts` — a `SchwarzOptions` variable carrying the tuning set (`Parts`,
  `Overlap`, `Eps`, `MaxRounds`, ...). It is a job input, not something a
  script constructs.

In a hand-written script the same call reads:

```
Blob:field = SchwarzSolve(model, opts);
```

The script compiles against three controllers besides Schwarz itself:
`Variables` for its value types, `Special` for the loop's control flow
(`Loop`, `If`, `Break`, `Return`) and `Grid` for the `Grid.ForEach` fan-out.
`Special` and `Grid` are host-only, so they must be present **on the server**
that compiles and runs the job — they are not manifest dependencies of the
Schwarz module, which also has to load on compute nodes that never receive
them.

## Where the files land

Inside the nupkg:

```
content/scripts/SchwarzSolve.wit
build/OutWit.Controller.Simulation.Schwarz.Scripts.targets
lib/net10.0/_._                     # framework-compatibility marker only
```

The `build/` targets auto-imports into any csproj that references the package.
Its `OutWitCopySchwarzScripts` target runs before `Build` and copies
`content/scripts/*.wit` flat into the consumer's output:

```
bin/Debug/net10.0/
  @Scripts/
    SchwarzSolve.wit
```

That flat `@Scripts/` folder is exactly what the server's script seeder reads
on startup. Copies are incremental (`SkipUnchangedFiles`), so a rebuild that
did not change the script text does not touch the staged file.

## Versioning and controller pairing

Current versions: this package **1.0.0**, controller
`OutWit.Controller.Simulation.Schwarz` **1.0.0** (the compatibility marker binds this
package to that controller line, `[1.0.0, 2.0.0)`), shared numerical core
`OutWit.Math.Simulation.Model` + `OutWit.Math.Simulation` **0.3.1** (contract and
numerics, versioned in lockstep). Both Simulation controllers and their Scripts
packages are published on nuget.org and on the OmnibusCloud organization feed; the
numerical core comes from the separate private WitMath feed.

**The script ships separately from the controller, and that is deliberate.**
A `.wit` script resolves activities by name at runtime, through the engine
compiler — there is no managed reference from this content-only package to the
controller DLL, and the two version independently on their own SemVer lines.
The compatibility surface is the activity names and their parameter
signatures, not the controller's binary API. See
`docs/adr-001-script-controller-version-compatibility.md` for the full policy.

The operational consequence is worth stating plainly: **upgrading the
Schwarz controller across a change to an activity's parameter list requires
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
