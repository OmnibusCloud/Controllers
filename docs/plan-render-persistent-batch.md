# Plan — Render persistent-batch (one Blender process per chunk)

Status: **SHIPPED + LIVE-VERIFIED** (2026-06-05). `OutWit.Controller.Render` **1.18.3** (1.18.0/1.18.1/1.18.2 superseded — see §8/§9). All three engines + tiles confirmed on the live macOS+Linux+Windows fleet; balance validated at production scale.
Owner: Dmitry + Claude.

> **Revision — macOS fix (1.18.2).** The first implementation (1.18.0/1.18.1) rendered a chunk via a
> Python loop calling `bpy.ops.render.render(write_still=True)` in one process. That crashed headless
> Blender on **macOS** (exit 134, `NSException`) — the render operator's file-save touches Cocoa. Live
> distribution tests against the deployed server caught it (two Cycles frame jobs failed on the Apple
> Silicon node; the benchmark's `write_still=False` render is fine, which is why the node still
> benchmarked). **Fix (D1):** FrameBatch now renders each contiguous frame chunk via Blender's
> command-line **animation render** (`-s START -e END -a`) — the GUI-free path that is macOS-safe and
> loads the scene once, using the *same* per-engine config as the working single-frame `-f` path (so
> Cycles / Eevee / Grease-Pencil behave identically). Tiles can't vary the border across an animation,
> so **tiled stills reverted to the per-tile path** (`Render.SplitTiles` + `Render.Frame`); tile
> batching is deferred. `Render.SplitTilesBatched` remains registered but unused. Verified by 28 real
> Windows renders (all 3 engines via `-a`) + live macOS re-test.

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

## 8. As-shipped corrections (1.18.1 → 1.18.3)

The plan above is as-built EXCEPT the render mechanism, which changed twice after live testing:

- **1.18.0 was unusable** — published before `Render.Model 1.1.0`, so its `>= 1.1.0` dep was
  unresolvable. Re-cut as **1.18.1** (publish Model + Scripts first, then the controller).
- **macOS NSException (→ 1.18.2 → 1.18.3, the important one).** The first `Render.FrameBatch`
  rendered a chunk with an in-process Python loop calling `bpy.ops.render.render(write_still=True)`.
  Live distribution tests caught it: on the Apple-Silicon node every Cycles frame job died with
  `exit 134 / libc++abi NSException` — the render-operator's file-save touches Cocoa headless on
  macOS. (The benchmark's `write_still=False` render is fine, so the node still benchmarked → only
  FrameBatch crashed; 28 Windows-only local renders never saw it.) **Fix (D1, user-chosen):**
  `RenderFrameBatchAsync` now renders each contiguous frame chunk via Blender's command-line
  **animation render** `-s START -e END -a -o "<dir>/f_"` — the GUI-free path (macOS-safe), scene
  loaded once, SAME per-engine config as the working single-frame `-f` path (so Cycles / Eevee /
  Grease-Pencil behave identically; GP = Eevee Next). The CLI render cannot vary the border across an
  animation, so **tiled stills reverted to per-tile** (`Render.SplitTiles` + `Render.Frame`);
  `Render.SplitTilesBatched` stays registered but unused. Shipped **1.18.3** (a `;` in the 1.18.2
  controller `<Description>` split the generated controller.json — MSBuild treats `;` as the item
  separator; fixed in `Build/OutWit.Controller.Manifest.targets` by escaping `;`→`%3B`).

Verified: 28 real Windows renders (all 3 engines via `-a`) + live re-test on the macOS fleet
(Cycles / Eevee / Grease-Pencil frames + tiled) all green.

## 9. Balance — validated at production scale

The whole motive was the Eevee/GP allocation inversion. Conclusion: **the render-only benchmark is
the right signal at render-bound scale, and persistent-batch makes it predictive — no benchmark
change needed.**

- **Why batching fixes it:** at chunk K>1 the per-chunk scene-load is amortised across K frames in one
  process, so the per-frame wall is render-dominated and the render-only Rate predicts it.
- **Render-bound, 3 nodes** — 1080p / 256 spp / 200-frame canonical wave (chunk=5): makespan 16m56s,
  per-node finishes within ~8% of each other; allocation tracked the Cycles benchmark (real per-frame
  throughput ≈ benchmark ratio).
- **Render-bound, 4 nodes** (added an RTX-class node) — same job: makespan 11m24s; allocation
  15:12:8:5 chunks (3080Ti : new : M4 : 1080Ti); finishes span ~1m41s on an ~11-min job — tight,
  despite the fleet being geo-distributed (Mac in the US, the rest in Israel) and one node running a
  local test suite concurrently. The new node slotted in by benchmark with zero manual tuning.
- **Small / cheap scale is the only soft spot (acceptable):** below the Cycles chunking threshold
  (`clamp(ceil(frames/48),1,8)` ⇒ chunk=1 under 48 frames) batching does NOT engage, so each frame
  pays full Blender spawn + scene load. There the per-frame wall is platform-bound (process spawn /
  disk), which the GPU render-only benchmark can't predict → mild inversion (e.g. a Windows node with
  a fast GPU but slow spawn looks faster than it renders). Cheap previews are quick anyway, so this is
  left as-is; the lever if ever needed is lowering the Cycles `TARGET` so it batches earlier (trades
  balance granularity). NOT changed.
