# OutWit.Controller.Sweep

Host-side orchestration of **parameter studies** over the
[`OutWit.Controller.CalculiX`](../OutWit.Controller.CalculiX/README.md) solve
controller: open a deck with baked placeholder tokens, submit the variant
table, and the pool computes the study — chunk by chunk, with everything
harvested so far observable while the sweep runs.

## Activities (all host-side)

| Activity | Purpose |
|---|---|
| `Sweep.Plan(baseDeck, opts) → SweepPlan` | Validates the study (variant/parameter arity, every token present in the deck) and computes the progressive chunk schedule. |
| `Sweep.InitState(plan) → SweepState` | The zero cursor. |
| `Sweep.ChunkCount(plan) → Int` | Loop bound of the bundled script. |
| `Sweep.MakeChunk(plan, state) → CcxTaskCollection` | Materializes the next chunk's variant decks by plain token substitution (this side never parses a deck) and builds their solve tasks. |
| `Sweep.Harvest(plan, state, wave) → SweepState` | Appends the chunk's results — states, measured solve times, response rows, artifact blob ids — to the manifest blob; returns the advanced cursor. Since 0.2.2 every index entry also carries the variant's label from its parameter values ("XMAX=300, T=250", `SweepVariantLabel`), so document clients - the ParaView plugin's variant picker - name variants instead of numbering them. |
| `Sweep.Finish(plan, state) → Blob` | Returns the final manifest blob. |

## Why chunks

A sweep chunk is a product checkpoint, not an algorithmic barrier: after every
chunk the manifest holds all harvested variants, so a cancelled sweep keeps
everything already computed, and a monitoring client sees per-variant states
mid-run. Sizing is progressive — small first chunk (feedback in minutes, a
broken scenario burns a handful of variants, not the night), geometric growth
to a cap (near-optimal allocator packing for the bulk; the cap bounds both
cancellation loss and a failed node's recompute radius).

A variant whose solver exits nonzero is recorded as a **failed row** in the
manifest — it never fails the task, so one bad variant cannot poison its node
batch.

## Module shape

Host-only (`IWitControllerHost` alone): never delivered to compute nodes,
which is what makes the declared dependencies on the host-only `Grid` and on
`Special` honest — dependency validation runs on nodes too, and a module that
carried node activities alongside these declarations would be refused there.
Node-side work lives entirely in `OutWit.Controller.CalculiX`.

## Script

`Scripts/SweepSolve.wit` (shipped via `OutWit.Controller.Sweep.Scripts`):
plan → loop chunks { make → `Grid.ForEach` → harvest } → finish.
