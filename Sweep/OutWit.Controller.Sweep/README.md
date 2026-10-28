# OutWit.Controller.Sweep

Host-side orchestration of **parameter studies** for every solver family the
host knows: submit a study - the parameters, the variant table and what the
variants run on - and the pool computes it chunk by chunk, with everything
harvested so far observable while the sweep runs.

| Family | The study's input | Node activity | Script |
|---|---|---|---|
| CalculiX | a base deck with baked tokens (a template study), or every variant's own deck (a deck set) | `Ccx.Solve` ([`OutWit.Controller.CalculiX`](../../CalculiX/OutWit.Controller.CalculiX/README.md)) | `SweepCalculiX.wit` |
| OpenFOAM | one case whose templated files carry the tokens | `Foam.Run` ([`OutWit.Controller.OpenFOAM`](../../OpenFOAM/OutWit.Controller.OpenFOAM/README.md)) | `SweepOpenFOAM.wit` |
| OpenFOAM | every variant's own ready case (a case set) | `Foam.Run` | `SweepOpenFOAMSet.wit` |

The study travels as `SweepOptionsData` (`OutWit.Controller.Sweep.Model`): the
parameters, the variant table, the chunk bounds and **exactly one family
block**. Everything else - the plan, the chunking, the state, the manifest,
the result index - is one implementation for every family; a family is an
`ISweepFamily` inside this controller (how its block is validated, how a
chunk becomes its node activity's tasks, how a result becomes a manifest row,
which artifacts a row offers).

An OpenFOAM **case set** is a second input beside the study,
`SweepOpenFOAMSetData`: one ready case per variant (its name, its tree, its
recipe, its size), no tokens and no parameters. The study's OpenFOAM block then
carries only what the cases share - threads, responses, artifacts - and the
variants carry no values. The set is a job input of its own script rather than
a member of the study, so the study's layout stays what every released host
reads: a host without case sets refuses the unknown script, not every study.

## Activities (all host-side)

| Activity | Purpose |
|---|---|
| `Sweep.Plan(opts) → SweepPlan`, `Sweep.Plan(opts, cases) → SweepPlan` | Validates the study (one family block, variants present with one value per parameter and unique indices) and its family block (CalculiX: every token in the base deck, or a deck set covering every variant exactly once; OpenFOAM: the case rules of `OutWit.Controller.OpenFOAM.Model` and every token placed in a templated file, every templated token declared; an OpenFOAM case set: every variant exactly one case, each case under the same rules and named in every finding, nothing templated, no tree or recipe in the block), naming every finding at once; then computes the progressive chunk schedule. The plan carries the case set. |
| `Sweep.InitState(plan) → SweepState` | The zero cursor, carrying the plan's chunk schedule - or, when the state variable already holds the cursor of an earlier run of the same study (see *Resuming a sweep*), that cursor, kept as it is once it is shown to fit the plan; a cursor that does not fit is refused by what is wrong with it. |
| `Sweep.ChunkCount(plan, state) → Int` | Loop bound of the bundled scripts: the chunks left after the cursor. `Sweep.ChunkCount(plan)` (the Sweep 2.0 - 2.1 form) still counts every chunk of the plan. |
| `Sweep.MakeChunk(plan, state) → <family>TaskCollection` | The next chunk's tasks, of the family's node activity: CalculiX variant decks materialised by plain token substitution (this side never parses a deck), deck-set decks as uploaded; OpenFOAM tasks as metadata only (the case and each variant's values - substitution happens on the node, so the base files travel once per node; in a case set each task is its own case with the block's shared part). Checks that the script's task collection holds the family's tasks and fails loudly otherwise. |
| `Sweep.Harvest(plan, state, wave) → SweepState` | Appends the chunk's rows - the sweep's verdict (succeeded, failed, refused) and the node's own result, verbatim - to the manifest blob, and returns the advanced cursor with the counts by outcome, the schedule the run goes by and the result index (variant, verdict, label from the parameter values - "XMAX=300, T=250" - or the case's name in a case set, and the artifacts by kind: `.frd`, `.dat`, a case zip). |
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

## The schedule rides the state

Since Sweep 2.2 the cursor carries the chunk schedule the run goes by
(`SweepStateData.ChunkSizes`, filled by `Sweep.InitState` and carried through
every `Sweep.Harvest`): the planned size of every chunk, summing to the variant
count, as `Sweep.Plan` computed it for this run - the client's bounds widened to
the fleet. A client reads the current chunk as `ChunkSizes[ChunkIndex]` and the
chunk count as `ChunkSizes.Count` and mirrors the server's plan from the state
alone; it must not recompute the schedule from the bounds it submitted, because
it does not know the fleet's width (WitSweep 1.6 did, and showed the wrong
tiles and "chunk 2 of 4" for a sweep the server had planned as three chunks).
The state's schedule is the schedule of record: `Sweep.MakeChunk` and
`Sweep.Harvest` take the chunk sizes from the state when it has them, from the
plan otherwise (a cursor an older host wrote).

**For state readers (WitSweep, the ParaView plugin, SweepLive):** the member is
appended at the end of `SweepStateData` (order 7). The state rides the default
MemoryPack format, whose reader refuses a payload with a member it does not
know, so a reader built against Sweep.Model 2.0 - 2.1 cannot decode a state a
Sweep 2.2 host writes (WitSweep names it: "the sweep's state could not be read
- member count 8 is larger than 7"). Roll out in this order: every state reader
on Sweep.Model 2.2.0 first, the host's Sweep 2.2 after. A 2.2 reader reads a
2.0 - 2.1 state with an empty schedule (the model guarantees it, never a null)
and then goes by the plan's. The document vocabulary stays `sweep.state@2`: the
schedule is an added optional field of the same document.

## Resuming a sweep

The script is resumable from its own state: a run whose `state` variable
already holds the cursor of an earlier run of the same study - set by whoever
resumes it (an engine that restarts a job from the last state it stored, a
client that re-submits with it) before the script runs - continues from that
cursor. `Sweep.Plan` plans again (the fleet may be a different width now),
`Sweep.InitState` keeps the handed cursor instead of the zero one once it fits
the plan - the handed schedule covers the study's variants, the next ordinal is
the sum of the chunks before the cursor, the counts add up, the manifest is
there - and `Sweep.ChunkCount(plan, state)` loops over the chunks after it
only, by the handed schedule. The chunks harvested before are never computed
again, the manifest grows from the handed one, and a cursor that does not fit
is refused in words before any node sees a task. A fully harvested cursor
leaves nothing to run: the script finishes with the manifest it was handed.

The engine side of this (handing the last state back to a rerun) is not in
WitCloud 2.4: a job that has started and then finds no eligible node fails by
name with its last state kept, and is not rerun from the beginning - what the
state points at stays readable at reattach. Resuming such a job is a client's
new submission with the state, once the engine takes initial variable values
with a submission.

## Result document

The state variable is rendered for document clients (the ParaView plugin's
cloud-open) as `sweep.state@2`: the counts by outcome, the manifest blob, the
chunk schedule and the result index with each variant's artifacts by kind, so a
client knows whether it downloads a CalculiX result file or an OpenFOAM case
zip. The
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

**Deploying Sweep 2.x:** the host needs both family modules next to it -
`CalculiX` (>= 1.1.0) and `OpenFOAM` (>= 1.0.0). The host loader refuses a
module whose declared dependencies are missing, so a host without the
OpenFOAM module refuses the whole Sweep module, and CalculiX sweeps stop with
it, even though they never touch OpenFOAM. Install (or upgrade) the OpenFOAM
module on the host before, or together with, Sweep 2.x.

## Scripts

`Scripts/SweepCalculiX.wit`, `Scripts/SweepOpenFOAM.wit` and
`Scripts/SweepOpenFOAMSet.wit` (shipped via `OutWit.Controller.Sweep.Scripts`):
plan → init state → count the chunks left → loop { make → `Grid.ForEach` →
harvest } → finish; they differ only in the declared task and result
collections, the node activity, and the case set the last one takes beside the
study.
