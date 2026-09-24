# OutWit.Controller.Sweep

Host-side orchestration of **parameter studies** for every solver family the
host knows: submit a study - the parameters, the variant table and what the
variants run on - and the pool computes it chunk by chunk, with everything
harvested so far observable while the sweep runs.

| Family | The study's input | Node activity | Script |
|---|---|---|---|
| CalculiX | a base deck with baked tokens (a template study), or every variant's own deck (a deck set) | `Ccx.Solve` ([`OutWit.Controller.CalculiX`](../../CalculiX/OutWit.Controller.CalculiX/README.md)) | `SweepCalculiX.wit` |
| OpenFOAM | one case whose templated files carry the tokens | `Foam.Run` ([`OutWit.Controller.OpenFOAM`](../../OpenFOAM/OutWit.Controller.OpenFOAM/README.md)) | `SweepOpenFOAM.wit` |

The study travels as `SweepOptionsData` (`OutWit.Controller.Sweep.Model`): the
parameters, the variant table, the chunk bounds and **exactly one family
block**. Everything else - the plan, the chunking, the state, the manifest,
the result index - is one implementation for every family; a family is an
`ISweepFamily` inside this controller (how its block is validated, how a
chunk becomes its node activity's tasks, how a result becomes a manifest row,
which artifacts a row offers).

## Activities (all host-side)

| Activity | Purpose |
|---|---|
| `Sweep.Plan(opts) → SweepPlan` | Validates the study (one family block, variants present with one value per parameter and unique indices) and its family block (CalculiX: every token in the base deck, or a deck set covering every variant exactly once; OpenFOAM: the case rules of `OutWit.Controller.OpenFOAM.Model` and every token placed in a templated file, every templated token declared), naming every finding at once; then computes the progressive chunk schedule. |
| `Sweep.InitState(plan) → SweepState` | The zero cursor. |
| `Sweep.ChunkCount(plan) → Int` | Loop bound of the bundled scripts. |
| `Sweep.MakeChunk(plan, state) → <family>TaskCollection` | The next chunk's tasks, of the family's node activity: CalculiX variant decks materialised by plain token substitution (this side never parses a deck), deck-set decks as uploaded; OpenFOAM tasks as metadata only (the case and each variant's values - substitution happens on the node, so the base files travel once per node). Checks that the script's task collection holds the family's tasks and fails loudly otherwise. |
| `Sweep.Harvest(plan, state, wave) → SweepState` | Appends the chunk's rows - the sweep's verdict (succeeded, failed, refused) and the node's own result, verbatim - to the manifest blob, and returns the advanced cursor with the counts by outcome and the result index (variant, verdict, label from the parameter values - "XMAX=300, T=250" - and the artifacts by kind: `.frd`, `.dat`, a case zip). |
| `Sweep.Finish(plan, state) → Blob` | Returns the final manifest blob. |

## Why chunks

A sweep chunk is a product checkpoint, not an algorithmic barrier: after every
chunk the manifest holds all harvested variants, so a cancelled sweep keeps
everything already computed, and a monitoring client sees per-variant states
mid-run. Sizing is progressive - the first chunk small enough for fast
feedback (a broken scenario burns a handful of variants, not the night),
geometric growth to a cap (near-optimal allocator packing for the bulk; the
cap bounds both cancellation loss and a failed node's recompute radius). A
chunk is also a wave: the grid places its tasks on at most as many machines as
the chunk has tasks, so the plan never opens narrower than the fleet -
`Sweep.Plan` asks the engine how many machines can take a task of the job
(local nodes and the handles other clouds offer) and raises the first chunk,
and the cap with it, to that width. A client's larger first chunk stays as
asked.

A variant that fails - a nonzero exit, a diverged solve - or that its node
refuses before running (OpenFOAM: run-time code, a step outside the
allow-list) is a **row** in the manifest with its verdict and reasons; it never
fails the task, so one bad variant cannot poison its node batch.

## Result document

The state variable is rendered for document clients (the ParaView plugin's
cloud-open) as `sweep.state@2`: the counts by outcome, the manifest blob and
the result index with each variant's artifacts by kind, so a client knows
whether it downloads a CalculiX result file or an OpenFOAM case zip. The
schema and the C++ and Python bindings are generated into
`OutWit.Controller.Sweep.Model/Documents/`.

## Module shape

Host-only (`IWitControllerHost` alone): never delivered to compute nodes,
which is what makes the declared dependencies on the host-only `Grid` and on
`Special` honest - dependency validation runs on nodes too, and a module that
carried node activities alongside these declarations would be refused there.
The families' node modules (`CalculiX`, `OpenFOAM`) are dependencies because
their task and result types ride through the scripts; node-side work lives
entirely in them.

## Scripts

`Scripts/SweepCalculiX.wit` and `Scripts/SweepOpenFOAM.wit` (shipped via
`OutWit.Controller.Sweep.Scripts`): plan → loop chunks { make → `Grid.ForEach`
→ harvest } → finish; the two differ only in the declared task and result
collections and the node activity.
