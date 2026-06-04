# Plan — Render persistent-batch (one Blender process per chunk)

Status: **DESIGN AGREED, not yet implemented** (2026-06-04). Target: `OutWit.Controller.Render` **1.18.0**.
Owner: Dmitry + Claude.

## 1. Problem

Today each distributed render task is a **single frame (or tile)**:

```
Blob:blend = Render.BuildBlendFromRefs(scene);
RenderTaskCollection:tasks = Render.Split(blend, startFrame, endFrame, options);   // one task per frame
RenderResultCollection:rendered = Grid.ForEach(task in tasks) => Render.Frame(task);
BlobCollection:result = Render.Collect(rendered, options);
```

`Render.Frame` → `BlenderRunner.RenderFrameAsync` spawns a **fresh Blender process that loads the
.blend and renders one frame**, then exits. So every frame pays full **Blender startup + scene
load**. Consequences (measured on the live 3-node cluster, 2026-06-04):

- **Slow.** A 23 MB scene is re-loaded 120× for a 120-frame job; the render itself is a fraction of
  per-frame wall time.
- **Balance inverts for Eevee / Grease Pencil.** These rasterizers render a frame in well under a
  second even at 1080p, so per-frame wall time is dominated by the fixed startup/context/load
  overhead — which is *node-dependent* and where an Apple M4 (unified memory / Metal) is far cheaper
  than a headless discrete-GPU Linux/Windows node. The benchmark measures *render-only* throughput
  (discrete GPUs win), so the allocator inverts the split: the M4 rendered Eevee at **0.86 s/frame**
  yet got **17** of 120 frames, while the slowest node got **61** and set the makespan.
- **Timeouts.** Heavy batches exceed the per-batch delivery timeout (separate fix below).

## 2. Decision

Render a node's chunk of frames in **one Blender process**: load the scene once, loop the chunk's
frames (`scene.frame_set(f)` + `bpy.ops.render.render`) — exactly what the redesigned **benchmark
script already does**. This is the user's "start Blender once, feed the batch" idea, realised as a
chunk-granularity activity rather than an IPC server.

### Grid is NOT touched (and must stay generic)

**`Grid` is a universal distribution controller for an unbounded range of tasks — it is NOT a render
controller.** Rendering *combines with* Grid exactly the way **Matrices** does
(`Grid.ForEach(task in tasks) => GustavsonMultiply(...)`): the domain controller supplies its own
activities and Grid distributes opaque collection items weighted by that activity's benchmark. So
the batching is **entirely Render-side** — `Render.Split` emits chunks, `Render.FrameBatch` consumes
them — and `Grid.ForEach` distributes chunks with no knowledge that they are render batches. Do NOT
push any render awareness into Grid.

## 3. Design (Render-side only)

1. **Model `RenderTaskBatchData`** — a chunk: `SceneBlobId` + an ordered list of the chunk's
   `RenderTaskData` (frames and/or tiles) + `Options`. (Frames in a chunk share one blend/scene.)
2. **`Render.Split`** groups its per-frame `RenderTaskData` into chunks of size **K** →
   `RenderTaskBatchCollection`. (Tiles likewise — see §5.)
3. **Scripts** (~28 `.wit`): `Grid.ForEach(batch in batches) => Render.FrameBatch(batch)` in place of
   `=> Render.Frame(task)`. Engine variants (`RenderFramesCycles/Eevee/GreasePencil`, video, still,
   tiled, dcc) updated the same way.
4. **`Render.FrameBatch`** (new activity, + per-engine variants mirroring `Render.Frame`): download
   the blend once, render every frame/tile in the chunk in **one** Blender process, return the
   per-frame results. Reuses `BlenderRenderArgsBuilder` + the benchmark's in-process render loop.
5. **`Render.Collect` / `Render.CollectTiles`** flatten the per-chunk result collection back to a
   flat frame/blob collection (minor change).
6. **`EstimateWork(batch)`** = Σ over the chunk's frames of `resX·resY·samples·tileFraction` (the
   existing per-frame work, summed) — so Grid's `Work/Rate` makespan model stays correct at chunk
   granularity.
7. **Benchmark — UNCHANGED.** The render benchmark already measures *many frames in one process*
   (`MeasureRenderAsync` / `BlenderBenchmarkScript`), which is **exactly** how `Render.FrameBatch`
   executes. So once execution is batched, the existing render-only `Rate` becomes representative on
   its own and the Eevee/GP inversion resolves. ⇒ **The separately-considered "wall-per-frame
   benchmark" (controller @v3) is no longer needed.**

`Render.Frame` (single) is **kept** for the single-frame still path and back-compat; `FrameBatch`
with one frame is equivalent.

## 4. Chunk size K — PER-ENGINE default (the one real tuning knob)

Trade-off: small K → finest balance but little amortization (≈ today); large K → max amortization
and a self-correcting benchmark, but coarser balance and a longer makespan tail (the last big chunk
on the slowest node). Extra nuance: the benchmark renders a *procedural* scene with **no .blend
load**, while `FrameBatch` loads the .blend once per chunk — so K must be large enough that the
load amortizes for the benchmark to match reality (matters most for fast engines / big scenes).

**The optimal K is engine-dependent**, because the overhead/render ratio is:

- **Cycles** (render-bound, heavy per-frame render): balance is already good at K=1; amortization is
  a speed bonus, not a correctness need, and a large chunk risks a long makespan **tail** (heavy
  frame × K on the slowest node). → **many small chunks** (balance-first).
- **Eevee / Grease Pencil** (overhead-bound, sub-second render): need a **large K** so the per-chunk
  scene-load amortizes and the engine becomes render-bound — *only then* does the Eevee/GP inversion
  resolve and the render-only benchmark become representative. Cheap frames mean a big chunk does not
  create a long tail. → **few large chunks** (amortization-first).

`Render.Split` already receives `options` (incl. `options.Engine`), so it derives the per-engine
default itself — **one place, no per-script duplication** — while `options.BatchSize` stays an
explicit override (the Blender plugin will NOT pass it, so the engine default applies; a script may
override). Default, no node-count needed:

```
(target, maxK) = Cycles            ? (TARGET_CHUNKS_RENDER_BOUND,   MAX_CHUNK_RENDER_BOUND)    // many small
              :  Eevee/GreasePencil ? (TARGET_CHUNKS_OVERHEAD_BOUND, MAX_CHUNK_OVERHEAD_BOUND)  // few large
K = options.BatchSize > 0 ? options.BatchSize : clamp( ceil(totalFrames / target), 1, maxK )
```

Starting tunables (validate on the live fleet, like the benchmark constants):
- **Cycles:** `TARGET_CHUNKS_RENDER_BOUND = 48`, `MAX_CHUNK_RENDER_BOUND = 8` (e.g. 120f → K=3, 40
  chunks; small chunks, fine balance, short tail).
- **Eevee/GP:** `TARGET_CHUNKS_OVERHEAD_BOUND = 8`, `MAX_CHUNK_OVERHEAD_BOUND = 48` (e.g. 120f → K=15,
  8 chunks; big enough that ~2 s scene-load amortises to a small fraction → render-bound → balance
  de-inverts).
- Floor 1 so small jobs still split across nodes (3f → K=1).

(One-chunk-per-node would maximise amortization but needs Grid to form per-node chunks — rejected:
keeps Grid generic.)

## 5. Tiles

`Render.SplitTiles` groups tiles the same way; `Render.FrameBatch` renders multiple tiles of a frame
in one process; `Render.CollectTiles` stitches as today. Same chunk-size policy.

## 6. Out of scope / separate

- **Per-batch delivery timeout fix (DONE, server-side, ships with the UI redeploy):**
  `WitNodesManager.TryDeliverBatchAndWaitAsync` registered the whole batch with the *per-task*
  `TaskTimeout` (5 min) → long multi-frame batches were killed mid-render. Now scaled by task count
  (`TaskTimeout × Requests.Count`). With persistent-batch a per-frame **heartbeat** (reset the
  timeout as each frame in the chunk completes) becomes natural and is the better long-term form.

## 7. Implementation order

1. `RenderTaskBatchData` model (+ Model README/version note).
2. `Render.FrameBatch` activity + adapter + per-engine variants + `EstimateWork`.
3. `Render.Split` → chunking (K default heuristic + `options.BatchSize`).
4. Scripts: `Grid.ForEach => Render.FrameBatch`.
5. `Render.Collect` / `CollectTiles` flatten.
6. Tests (unit: chunking + EstimateWork; live: re-run the 3-node Eevee/Cycles cases — expect the
   Eevee split to de-invert and big speedup from single scene-load).
7. Bump controller 1.18.0; push; publish; bump WitCloud ref; redeploy with UI.
```
