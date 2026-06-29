# Extending the Render Controller to Support More Scenes

**Scope:** `OmnibusCloud/Controllers` → `Render` controller.
**Premise:** Rendering is **distributed**. Single-node fallback (run the whole animation sequentially on one node) is explicitly **out of scope** — it defeats the purpose of the network. Every capability below must preserve per-frame / per-tile distribution.
**Revision:** 2026-06-28 — rewritten against the actual codebase (ground-truth audit). Where this revision corrects the original draft, see §0. File/line citations are as of the audit date; treat line numbers as navigational hints, not contracts.
**Revision 2:** 2026-06-28 — a live test overturned the "transport is wired end-to-end for Blender" claim. Node-side attachment delivery was **missing**; it is now implemented (Stage 1, Render 1.21.0). See §0.0, which supersedes the affected parts of §0.2 / §2 / Phase D / E-full.
**Revision 3:** 2026-06-29 — **Stage 2 (E-full for prebaked fluid) shipped and proven live.** Per-frame cache slicing + a baked-fluid validator-accept path now let a prebaked Mantaflow smoke sim render distributed. See §0.1.

---

## 0.1 Revision 3 — Stage 2: per-frame cache slicing + prebaked fluid, proven live (supersedes the E-full "deferred" status for the prebaked path)

E-full's per-frame addressing is now **built and proven on the deployed server** for the *prebaked* simulation path. A user uploads a `.blend` whose simulation is already baked to disk, plus the per-frame cache files as attachments; the controller slices the cache per frame so each node downloads only the frames it renders.

**Shipped (Render.Model 1.5.0 / Render 1.22.1; on `main`; validation suite 82/82 green):**
- **Per-frame slice field** — `RenderSceneAttachmentRefData.Frame (int?)` (`null` = global/every-frame asset; an int = that cache file belongs only to that frame). Threaded through `Is()` / `Clone()` / MemoryPack (appended for wire back-compat).
- **Per-frame slicing in the split** — `Render.SplitBatched` gives each chunk `Frame == null` globals **plus only its own frames'** cache; `Render.SplitTiles` gives each tile the global assets plus that single frame's cache. So a node never receives another frame's VDB.
- **Baked-fluid validator-accept** — `BlenderValidationScript.cs` now admits a `FLUID` domain when it is **baked** *and* its cache files are **attached** under the domain's `cache_directory` (mirrors the `MESH_SEQUENCE_CACHE` allow-when-attached shape). Unbaked or unattached fluid still blocks with an actionable message. **Blender-5.1 fix:** the baked-state attribute is `has_cache_baked_data` (the old `is_cache_baked_data` raised `AttributeError` on 5.1, so the accept path silently never fired) — both the validator and the bake fixture now read `has_`-first with an `is_`-fallback.

**Key architecture insight:** prebaked fluid needs **no new activity or script** — the existing `RenderVideoCycles` flow (`BuildBlendFromRefs → SplitBatched → Grid.ForEach(FrameBatch) → Collect → EncodeVideo`) plus the Stage-1 node materializer plus the slicing above handle it end-to-end. A relative `cache_directory` (Blender's default `//cache_fluid`) resolves on the node as-is via Stage 1.5's `make_paths_relative`.

**Proven live 2026-06-29 (deployed image `v1.6.24-beta`, `engine.omnibuscloud.com`):** a headless-baked 24-frame Mantaflow smoke domain (OpenVDB, `cache_type='ALL'`) uploaded with 24 `Frame`-tagged `FluidCache` attachments. Results: `RenderValidateBlend → IsValid:true` (baked+attached fluid accepted); `RenderStillCycles @ f20 →` visible smoke, **byte-identical to a local single-machine baseline** (proves the right frame's VDB was sliced, delivered, materialized, and read); `RenderVideoCycles 1–24 →` smoke that **evolves frame-to-frame** (proves per-frame slicing across nodes). Fixture/client persist in `scratchpad/fluidtest` + `scratchpad/fluid_stage` + `scratchpad/LiveTest`.

**What this leaves for the full production release (next phase — see Phase F):** the **delegated bake path** — let a user submit an *unbaked* sim and have the controller bake it on the network. The bake is a host-of-the-job concern that must run on **one** node (sequential), so it dispatches via **`Grid.Delegate`**, which already selects the single fastest compatible node using the *same* `WitGridTaskAllocator` benchmark-rate model as `Grid.ForEach` distribution. Then the per-frame VDB it produces flows into the slicing above. Plus: wiring a "bake on a node" option into the Blender add-on + bridge, and live-proving on the real Mantaflow demo scenes from §7.1. Deferred beyond that: absolute `cache_directory` portability (`make_paths_relative` does not touch `FluidDomainSettings.cache_directory`); other sim types (cloth/liquid/fire) reuse these same rails.

---

## 0.0 Revision 2 — the node-side attachment-delivery gap (supersedes the "wired end-to-end" claim)

A live test against the deployed server overturned this plan's central transport claim. §0.2 and §2 stated the attachment rails were "complete and wired end-to-end for the Blender path." They were **host-side only**: `Render.BuildBlendFromRefs` materializes + remaps attachments and `Render.ValidateBlend` passes — but a remote render **node** only downloads the scene blob into an isolated per-blob cache dir (`BlobCacheService`, `OutWit.Cloud.Client`) and never received the attachment blobs. `RenderTaskData` / `RenderTaskBatchData` carried no attachment refs and nothing materialized node-side (`ProcessingRunner` / `WitEngineNode` do no scene prep). **Verified empirically:** a scene linking an emission sphere from an attached external library validated host-side `{IsValid:true}` but rendered **empty** on the worker node (fixture + images in `scratchpad/attachtest`; jobs validate `40a92121`, still `f4f5f9e9`).

**Consequence:** Phase D was *not* actually shipped for distributed rendering — it worked only in-process (host storage == node storage) and in host-side validation. The same gap sat under **E-lite** (the attached `.abc` never reached the node) and is the foundation **E-full** builds on.

**Fixed — Stage 1 (commit `6eed0b6`; Render 1.21.0 / Render.Model 1.4.0; 40 local tests green):**
- `RenderTaskData.Attachments` + `RenderTaskBatchData.Attachments` (appended for MemoryPack wire back-compat).
- `Render.SplitBatched` / `Render.SplitTiles` read the `<blend>.attachments.json` sidecar (written by `BuildBlendFromRefs`) and thread the refs onto every task / chunk — defensive, never a new split-time failure.
- `Render.Frame` / `Render.FrameBatch` copy the blend to a working dir, download + materialize each attachment at its `RelativePath`, then render (gated on `Attachments.Count > 0` → self-contained rendering byte-for-byte unchanged), with best-effort cleanup.
- Shared `RenderSceneAttachmentTransfer` util (sidecar read + materialize + path-traversal guard) + unit/split tests.

**Revised phase status (read the phases below through this lens):**
- **Phase D** — now genuinely complete for the Blender *distributed* path, including arbitrary (absolute-ref) scenes as of **Stage 1.5** (Render 1.21.1): `BlenderSceneAttachmentRemapHelper` now calls `bpy.ops.file.make_paths_relative()` after materialization so the prepared blend carries `//`-relative dependency paths, which resolve on both host and node (the node materializes the same relative layout next to its working copy). Verified headless: absolute dep path → `//deps/lib/…` after remap.
- **E-lite** — the model / Split / node plumbing it depends on is now in place, so an attached `.abc` reaches the node; the validator allow-when-attached change in §E-lite still stands on its own.
- **E-full** — the "linchpin does not exist" note in §E-full is now *partly* resolved: per-task / per-batch attachment refs EXIST and `Render.Split*` thread them. What remains for E-full is the **per-frame `Frame (int?)` slice field** (so each node fetches only its frames' cache) + the **host bake activity** (`Render.BakeSimulation`, dispatched via `Grid.Delegate`) + the two simulation scripts (delegated / prebaked).

---

## 0. What changed in this revision (ground-truth corrections)

Three claims in the original draft were out of date or imprecise. Correcting them changes the plan's cost and sequencing:

1. **There is no `validate_blend.py` file.** The Blender validation script is *generated* as a C# list of source lines by `BlenderValidationScript.BuildScript()` ([`Render/OutWit.Controller.Render/Utils/BlenderValidationScript.cs`](../Render/OutWit.Controller.Render/Utils/BlenderValidationScript.cs)) and written to a randomly-named temp `.py` at runtime by `BlenderRunner` ([`Utils/BlenderRunner.cs`](../Render/OutWit.Controller.Render/Utils/BlenderRunner.cs), ~`:256-263`). Every "edit the validation Python" task in this plan means **editing that C# string list**, not a standalone file. There is nothing to run or hand to a test scene standalone.

2. **The transport rails (Phase D) are not "half-laid" — they are complete and wired end-to-end for the Blender path.** *(Revision 2 correction — this held **host-side only**; render nodes did not receive attachments until Stage 1. See §0.0.)* The Blender add-on already *collects* every attachment kind into `<blend>.attachments.json` (`OutWit.Render.BlenderAddon/outwit_render_bridge/bridge_scene_attachments.py`, `collect_scene_attachment_metadata()`), uploads each as a blob, and submits the manifest. The controller already *materializes + remaps + validates* them ([`Adapters/WitActivityAdapterRenderBuildBlendFromRefs.cs`](../Render/OutWit.Controller.Render/Adapters/WitActivityAdapterRenderBuildBlendFromRefs.cs); [`Utils/BlenderSceneAttachmentRemapHelper.cs`](../Render/OutWit.Controller.Render/Utils/BlenderSceneAttachmentRemapHelper.cs); `LoadSupportedAttachedPaths` in `BlenderValidationScript.cs`). So Phase D is **largely already shipped for Blender**; the remaining D work is 3ds-Max coverage + minor polish, not net-new infrastructure.

3. **Phase E's blocker is a validator contradiction, not a missing capability.** Alembic `.abc` already transports as the `CacheFile` kind and **already passes validation when attached** (proven by `RenderValidateBlendTransferredCacheBlenderTests`). The only thing blocking Alembic scenes is the *unconditional* `MESH_SEQUENCE_CACHE` modifier block in the validator, which never consults the attachment manifest. Phase E therefore splits into **E-lite** (remove that contradiction — a few controller-side lines, this iteration) and **E-full** (per-frame cache slicing — a genuine model re-architecture, deferred).

Two smaller facts that shape the work:

- **The `Render.ValidateBlend` result crosses the wire as a JSON string**, not a MemoryPack object. The `.wit` returns `String:result` ([`Scripts/RenderValidateBlend.wit`](../Render/OutWit.Controller.Render/Scripts/RenderValidateBlend.wit)); the adapter does `JsonSerializer.Serialize(validation)`; the bridge re-parses it. So adding finding categories (Phase A) is a **JSON-contract** change across ~4 consumers, with **no MemoryPack wire concern** — the `[MemoryPackable]` on `RenderValidateBlendData` is not exercised for this activity's transport.
- **`Kind` / `PackagingStrategy` are free-form strings, not a C# enum**, duplicated across the validator (`BlenderValidationScript.cs`), the remap helper (`BlenderSceneAttachmentRemapHelper.cs`), and the initiator (`bridge_scene_attachments.py`). Adding/renaming a kind touches all sites with **no compile-time linkage** — budget for it whenever a phase "adds a kind".

**Net effect on sequencing:** the highest-ROI work for "unblock attractive portal scenes" is controller-side and cheaper than the original draft implied — **A → C → D-verify → E-lite**, with E-full / F / G demand-driven.

---

## 1. First principle: the frame-independence invariant

The render flow is `Render.Split → Grid.ForEach(Render.Frame) → Render.Collect`. Each node renders **one frame** (or one tile) from the **same prepared `.blend`**, with no shared state between nodes.

> **Invariant.** Frame *N* must be fully determined by `(prepared .blend) + (frame index N) + (per-frame assets transported to that node)`, independent of frames `1…N-1`.

> **Wired-flow note.** The scripts end users actually run use the **batched** variants: `Render.BuildBlendFromRefs → Render.SplitBatched → Grid.ForEach(Render.FrameBatch) → Render.Collect` ([`Scripts/RenderFrames.wit`](../Render/OutWit.Controller.Render/Scripts/RenderFrames.wit), [`Scripts/RenderStill.wit`](../Render/OutWit.Controller.Render/Scripts/RenderStill.wit)). Both the per-frame and batched families exist. **Any per-frame-addressing change (E-full) must touch both `RenderTaskData` and `RenderTaskBatchData`.**

This gives exactly **two** admissible ways to support a scene that is blocked today, without breaking distribution:

1. **Transport** — the scene depends on frame-independent external data (textures, fonts, OpenVDB volumes, image sequences, linked libraries, Alembic caches). The data is deterministic per frame; it just needs to reach the node. *The attachments infrastructure for this is built and wired for Blender* (see §3).
2. **Bake-to-frame-addressable** — the scene contains a *sequential* simulation (fluid, smoke, cloth, dynamic particles, soft/rigid body, GN simulation zones). It is converted, in a **host-side pre-pass**, into a per-frame cache that is itself frame-independent (each node reads only its own frame's baked slice), then transported.

What is **never** admissible distributed: a simulation left *unbaked* at render time. Its frame *N* depends on prior frames, which the node cannot see. Baking is mandatory and is a host concern, never a node concern.

Everything in this plan is one of those two moves.

---

## 2. Where we are today (verified against code)

**`Render.ValidateBlend` currently BLOCKS** (issue → `isValid=false`), via the generated validation script in `BlenderValidationScript.cs` (modifier/dependency checks, ~`:245-273` for the simulation block list). All confirmed against source:

- `PARTICLE_SYSTEM` modifier — **blanket** (every particle system, including static scatter), keyed off `mod.type` only with no settings inspection.
- `FLUID` modifier — with cache-baked / cache-directory checks (`is_cache_baked_data`, and `is_cache_baked_mesh` when `use_mesh`). This is the *only* sim type with a bake-awareness branch.
- `CLOTH` modifier — unconditional.
- `MESH_SEQUENCE_CACHE` modifier (Alembic / USD) — unconditional ("Geometry cache … not yet portable"), **even when the `.abc` is attached** (this is the E-lite contradiction).
- Any **missing** external dependency (image, library, font, movie clip, sound, cache file, volume, image sequence, VSE media) → issue.

External dependencies that *exist but are not inlined* are **warnings, not blocks**. An attached dependency (present in `<blend>.attachments.json` with `PackagingStrategy == "SceneAttachmentBlob"`) **passes** instead of warning, via `LoadSupportedAttachedPaths(...)` (`BlenderValidationScript.cs` ~`:328-368`, with per-kind allow-branches throughout the script).

**Finding data model — `RenderValidateBlendData`** ([`Render/OutWit.Controller.Render.Model/RenderValidateBlendData.cs`](../Render/OutWit.Controller.Render.Model/RenderValidateBlendData.cs)): just `bool IsValid` + `List<string> Issues` + `List<string> Warnings`. **No category, no severity, no per-finding type** — the only severity signal is *which list* a string lands in. `IsValid` is computed in Python as `len(issues) == 0`. The result is serialized to JSON and consumed by: `ParseResult` (in `BlenderValidationScript.cs`), `RenderValidateBlendData`, the bridge's deserialize (`BlenderBridge/Services/Render/BridgeRenderLaunchService.cs`), and a second flat DTO `RenderValidateBlendResponse` (`BlenderBridge/Contracts/`). Plus `Is()`/`Clone()` overrides on both models.

**Infrastructure already built AND wired end-to-end (this is the important part):**

- **Attachments manifest** — `<blend>.attachments.json`. Entry type `RenderSceneAttachmentRefData` (`Kind`, `BlobId`, `OriginalPath`, `RelativePath`, `PackagingStrategy` — all strings). Dependency kinds in use: `CacheFile`, `Volume`, `LinkedLibrary`, `ImageSequenceFrame`, `MovieClip`, `Sound`, `VseImageStripFrame`, `VseMovieStrip`, `VseSoundStrip`, `Font` (+ `ImageAsset`, which uses `PackedBlendCopy` not blob transport). Packaging strategy `SceneAttachmentBlob`.
- **Collector (initiator)** — `bridge_scene_attachments.py` `collect_scene_attachment_metadata()` walks `bpy.data.{images,fonts,cache_files,libraries,volumes,sounds,movieclips}` + VSE strips and emits manifest entries; `bridge_operators.py` uploads each blob and ships the manifest into validate/render. **3ds-Max** has its own collector (`MaxConnectedRenderSceneAttachmentService.cs`) but it covers **only `ImageAsset`**.
- **Transport hub (controller, host)** — `WitActivityAdapterRenderBuildBlendFromRefs` downloads attachment blobs, materializes them next to the blend, remaps in-scene paths via headless Blender, and writes the `<blend>.attachments.json` sidecar. The output is a **single self-contained blend blob** whose `Guid` flows into `Render.Split*`.
- **Node-side path remap** — `BlenderSceneAttachmentRemapHelper` / `SceneAttachmentPathRemapEntry` rewrite external paths (libraries, fonts, sounds, clips, volumes, cache_files, images, VSE) so they resolve. (In the wired flow remap actually happens **host-side, before split**, as part of producing the self-contained blob.)

So the rails for **transport** are **laid and shipping for Blender**. "Blocks" that remain are validator *policy* (Phase A/C) or kinds the initiator doesn't yet collect on **non-Blender** initiators (3ds-Max).

**Validator blind spots (must be fixed first — see Phase A).** These pass `ValidateBlend` today but **render incorrectly when split across nodes**, because they carry sequential per-frame state and are not inspected (grep-confirmed absent from the validator):

- Rigid body (object-level `obj.rigid_body`, not a modifier → never inspected).
- Soft body (`SOFT_BODY` modifier — not in the block list).
- Dynamic Paint (`DYNAMIC_PAINT` modifier — not in the block list).
- Mesh Cache `.mdd/.pc2` (`MESH_CACHE` modifier — distinct from the Alembic `MESH_SEQUENCE_CACHE` that *is* blocked; the `bpy.data.cache_files` loop covers Alembic/USD datablocks, **not** the MeshCache modifier).
- Geometry Nodes **Simulation Zones** (`NODES` modifier with a simulation-output node — node groups are never walked; procedural GN already passes precisely because `NODES` is ignored, which is also why *sim-zone* GN silently passes).

You cannot expand support on a faulty oracle. Fixing classification is the prerequisite for everything else.

---

## 3. Feature taxonomy by distribution behavior

| Scene feature | Frame-independent? | Admit via | Status today | Effort |
|---|---|---|---|---|
| Packed textures / materials / shaders | Yes | already works | passes | none |
| External textures / fonts (present on disk) | Yes (needs transport) | auto-pack **or** attach | attach works; pack = Phase B | cheap |
| External image sequence (texture) | Yes (needs transport) | attach | **works when attached** (Blender collects) | done (verify) |
| External OpenVDB volume (static) | Yes (needs transport) | attach | **works when attached** (Blender collects) | done (verify) |
| Linked library (`.blend` link) | Yes (needs transport) | attach **or** make-local | **works when attached** (Blender); recursive deps unverified | low |
| Procedural Geometry Nodes (no sim zone) | Yes | already passes (`NODES` ignored) | passes | none (verify) |
| **Static particle scatter** (non-dynamic Hair) | **Yes** (deterministic) | refine validator | **blocked (blanket)** | med · Phase C |
| **Alembic / mesh-sequence-cache** (baked geometry) | **Yes** (per-frame addressable) | attach (transport done) + validator allow | **blocked by modifier check, though `.abc` transports** | **low (E-lite)** · high value |
| Dynamic particles / hair dynamics | No (sequential) | bake → Alembic + transport | blocked | high |
| Fluid / smoke / fire (Mantaflow) | No (sequential) | bake → OpenVDB sequence + per-frame transport | blocked | high (data volume) |
| Cloth / soft body | No (sequential) | bake → Alembic / point cache + transport | cloth blocked; soft body **silently passes (wrong)** | high |
| Rigid body | No (sequential) | bake → keyframes / Alembic + transport | **silently passes (wrong)** | med–high |
| GN Simulation Zones | No (sequential) | bake → GN cache / Alembic + transport | **silently passes (wrong)** | high |
| Dynamic Paint | No (sequential) | bake + transport | **silently passes (wrong)** | high |

The dividing line is not "simulation vs not" — it is **frame-independent vs sequential**. Static particle scatter and Alembic are *simulation-adjacent yet frame-independent*, which is why they are the cheap, high-value wins.

---

## 4. Phased plan

Each phase preserves distribution. For each: goal, **where the work lives (controller / initiator)**, controller changes, how distribution is kept, risk, effort, test scenes, acceptance.

### Phase A — Fix the validation oracle *(foundation · controller-only · days)*

**Goal:** make `ValidateBlend` correctly classify what is safe to distribute, before relaxing anything. Today five sequential-state features silently pass and render wrong; convert them to honest blocks and give every finding an actionable category.

**Where:** **Controller-only** for detection + categorization (the category is *produced* node-side in the generated script and surfaced through JSON). Display of categories is initiator/portal, but no initiator change is required for A to function.

**Changes**
- **Detection** in `BlenderValidationScript.cs`:
  - `SOFT_BODY`, `DYNAMIC_PAINT`, `MESH_CACHE` — clone the existing `if mod.type != 'X': continue` pattern (~3-6 lines each).
  - Rigid body — a **new object-level scan**: `if getattr(obj, 'rigid_body', None) is not None: …` (it is `obj.rigid_body`, not a modifier).
  - GN Simulation Zones — the only non-trivial piece: enumerate `NODES` modifiers, get `mod.node_group`, walk `node_group.nodes` for a simulation-output node (e.g. `bl_idname == 'GeometryNodeSimulationOutput'`), recursing into nested groups with a visited-set guard.
- **Finding categories** — add a category per finding (`TransportableDependency` / `RequiresBake` / `Unsupported`). **Do it additively** (parallel category lists alongside `Issues`/`Warnings`, or a structured findings list emitted as a new JSON field) to avoid breaking the existing flat JSON contract. Update `RenderValidateBlendData`, `ParseResult`, the bridge deserialize, the `RenderValidateBlendResponse` DTO, and both `Is()`/`Clone()` overrides.
- **Messages** must say *what to do* ("bake to cache and re-submit", "pack resources", "export to Alembic").

**Distribution:** N/A (gate only).
**Wire-compat:** none on MemoryPack (JSON-string transport). The real surface is the JSON shape across the ~4 consumers above. **Risk:** low *with the additive approach*; reshaping findings into objects is a breaking JSON change — avoid. **Effort:** days; the GN sim-zone node-graph walk is the bulk.

**Test scenes**
- Rigid-body domino / fracture *(construct, or Cell Fracture demo)* → expect **block**, category `RequiresBake`.
- GN sim zone: `geometry-nodes/simulation/2D_smoke_simulation.blend` (Blender demo) → expect **block**.
- Soft-body / dynamic-paint scene *(construct)* → expect **block**.
- Control: Classroom (CC0), BMW27 (CC0) → still **pass** unchanged.

**Acceptance:** every blind-spot scene is flagged with the correct category and an actionable message; no regressions on clean controls (golden-file).

---

### Phase B — Auto-pack on submit *(cheap · initiator-only · days)*

**Goal:** eliminate the most common failure — missing/unpacked textures — for the packable subset.

**Where:** **Initiator-only.** The pack mechanism already exists in the Blender add-on (`bridge_scene_packaging.py`, `bpy.ops.file.pack_all()`, wired in `bridge_operators.py`). A fully-packed `.blend` already validates today. **Zero controller change.**

**Changes**
- Make **File → External Data → Pack Resources** the default before upload (textures, fonts, most images); cap and warn on `.blend` size inflation.

**Distribution:** packed data travels inside the `.blend`; every node is self-contained. Fully preserved.
**Risk:** low. **Effort:** days. (Note: this is in the **Blender repo**, not Controllers — out of this repo's scope but listed for completeness; for 3ds-Max the equivalent is its own exporter.)

**Test scenes**
- A deliberately **unpacked** variant of Classroom / BMW → after auto-pack: **pass** + byte-identical render to the packed original across nodes.

**Acceptance:** unpacked-texture scenes pass after auto-pack and render identically on ≥2 nodes.

---

### Phase C — Static particle scatter *(medium · controller-only · weeks · highest visual payoff)*

**Goal:** stop blocking deterministic Hair/scatter. Unlocks a large share of *attractive legacy* scenes (pebbles, grass, static fur, turntables).

**Where:** **Controller-only.** The block is pure validator policy; the initiator does not classify particles (its dependency policy only string-matches the controller's returned warning text).

**Changes** — refine the blanket `PARTICLE_SYSTEM` block in `BlenderValidationScript.cs`:
- **Allow** when `settings.type == 'HAIR'`, hair dynamics disabled (`use_hair_dynamics` off), and no baked/external point-cache dependency → geometry is regenerated deterministically at render time.
- **Keep blocking** Emitter systems and any dynamics-enabled hair (sequential) → route to bake (Phase F/G).
- **Implementation note:** iterate `obj.particle_systems` and inspect `psys.settings` / `psys.point_cache` directly — a `PARTICLE_SYSTEM` modifier can back **multiple** particle systems, so the modifier-keyed loop is not 1:1. Surface the decision via the Phase A category (`TransportableDependency` vs `RequiresBake`).

**Distribution:** non-dynamic Hair is deterministic → every node regenerates identical geometry → safe to split.
**Risk:** medium — classifying "safe vs unsafe" precisely; hair seeded on animated/deforming meshes is still per-frame deterministic but verify child/seed/`frame_start-end` interplay. **Effort:** ~1-3 weeks incl. tests.
**Note (bounded payoff):** legacy particle hair is EOL upstream; the modern hair system is Curves + Geometry Nodes (a `NODES` modifier the validator already ignores, i.e. already passes). So this mostly rescues **legacy** scenes — useful but bounded. No model change required for the minimal version.

**Test scenes**
- **Pabellon Barcelona** (CC-BY) — pebbles via particle system → **pass** after change.
- **The Junk Shop** (CC-BY) — hair-particle detailing (static) → **pass**.
- **Fishy Cat** (CC0) — static fur → **pass**.
- **Lone Monk** (CC0) — confirm grass is particle-scattered; if so → **pass**.
- Negative control: **Spring** (CC-BY-SA) — dynamic hair → still **block**.

**Acceptance:** scatter scenes pass and render perceptually-identical across nodes (golden-file); dynamic-hair scene stays blocked.

---

### Phase D — Transportable external assets via attachments *(host-side shipped pre-Stage-1; node-side delivery added in Stage 1 — see §0.0)*

**Goal:** admit frame-independent external dependencies that cannot be packed into the `.blend`.

**Where & status:** For **Blender**, the controller consumer side (allow-when-attached + materialize + remap) and the initiator collector are **both already implemented** for OpenVDB volumes, image sequences, linked libraries, movie clips, sounds, fonts, cache files, and VSE media. So Phase D is largely **verification**, not new build, on the Blender path.

**Remaining work**
- **Initiator (3ds-Max):** the Max collector handles only `ImageAsset` — bring it up to Blender's coverage if Max scenes need volumes/libraries/caches. (3ds-Max repo.)
- **Controller (small):**
  - UDIM tiles currently always warn with no attached-path allow-branch — add one if UDIM scenes are in scope.
  - Recursive **linked-library** dependencies (a linked `.blend` that itself references external files) are unverified — confirm or handle.
  - Optional **per-asset checksum** verification (today only `BlobId`; the manifest carries no hash).
- **Cross-cutting:** there is no central enum — any new kind must be added consistently in the validator, the remap helper, and each initiator collector.

**Distribution:** these assets are identical for every frame; transported once per node (materialized into the prepared blob pre-split). Fully preserved.
**Risk:** low–medium (mostly completeness/verification). **Effort:** small on the Blender path; the bulk is 3ds-Max parity.

**Test scenes**
- Static **OpenVDB volume** scene → pass + correct render with volume attached.
- **Image-sequence texture** scene → pass with the sequence attached.
- **Linked-library** scene → pass with the library attached.

**Acceptance:** each transported-dependency scene passes and renders correctly on ≥2 nodes.

---

### Phase E — Alembic / mesh-sequence-cache *(split: E-lite now, E-full deferred)*

The original "Phase E" conflated two very different costs. They are now separated.

#### E-lite — admit attached Alembic *(controller-only · low effort · this iteration)*

**Goal:** stop blocking Alembic / mesh-sequence-cache scenes whose `.abc` is already attached and transportable.

**Root cause:** the `.abc` file already transports as the `CacheFile` kind and **already passes** when attached (test: `RenderValidateBlendTransferredCacheBlenderTests`), and its path is already remapped (`BlenderSceneAttachmentRemapHelper`, cache_file branch). The **only** blocker is the unconditional `MESH_SEQUENCE_CACHE` *modifier* check, which ignores the manifest.

**Changes** — in `BlenderValidationScript.cs`, change the `MESH_SEQUENCE_CACHE` modifier loop from unconditional block to **allow-when-attached**: resolve `mod.cache_file.filepath`, normalize, and check membership in the `supported_attached_cache_paths` set already built for the `cache_files` loop. (Same shape applies to `MESH_CACHE` `.mdd/.pc2` once that kind is collected.)

**Distribution:** the whole `.abc` is materialized into / next to the prepared blend before split → every node renders its frame from the same self-contained blob. Distribution is **preserved**; the cost is that each node receives the **entire** cache. Acceptable for small/medium caches.

**Risk:** low. **Effort:** a few lines + a golden-file test. **Acceptance:** an Alembic-backed scene with the `.abc` attached passes and renders per-frame-correct on ≥2 nodes.

**Test scenes**
- A cloth or character animation exported to Alembic *(construct: bake → File → Export → Alembic → re-import via Mesh Sequence Cache)* → **pass** after change, per-frame correct.

#### E-full — per-frame cache slicing *(deferred · model re-architecture)*

**Goal:** stream only frame *N*'s slice of a large cache to node *N*, so big Alembic/VDB sequences stay distributable without shipping the whole cache to every node.

**Why deferred:** *(Revision 2 — the attachment-plumbing half is now DONE in Stage 1: `RenderTaskData` / `RenderTaskBatchData` carry `Attachments` and `Render.Split*` thread them; what remains for E-full is the per-frame slice field + the host bake activity. See §0.0.)* As originally written, the linchpin did not exist: `RenderTaskData` carries only `SceneBlobId` + `Frame` (no per-frame attachment reference); `Render.Split*` never receives `AttachedFiles`; `BuildBlendFromRefs` materializes whole attachments **before** split. Building per-frame addressing requires: a frame field on the attachment record, per-frame attachment refs on **both `RenderTaskData` and `RenderTaskBatchData`**, attachment-partitioning in `Render.Split*` / `Render.SplitBatched`, and a new **host-side bake activity** `Render.BakeAlembic` (does not exist — mirror `Render.BuildBlendFromRefs`: a host activity that drives headless Blender and re-uploads a blob; slot it between `BuildBlendFromRefs` and `Split*`). For a **monolithic** `.abc`, "fetch only your frame's slice" is impossible without re-exporting to a per-frame file sequence — at which point E-full converges with Phase F's per-file model. Take this up only when real cache size forces it.

---

### Phase F — Baked physics caches: fluid / smoke as OpenVDB sequence *(prebaked path DONE — Stage 2 / Rev 3; delegated-bake path = current release phase)*

**Status (Rev 3):** the **prebaked** half is shipped and proven live (see §0.1): per-frame VDB slicing + baked-fluid validator-accept render a user-baked Mantaflow smoke sim distributed. The **remaining** release work is the **delegated bake** — accept an *unbaked* sim, bake it once on the network via `Grid.Delegate` (best node by benchmark rate, same model as distribution), emit a per-frame `Frame`-tagged VDB manifest, then feed the existing slice→ForEach→Collect flow — plus add-on/bridge UX and live proof on the real demo scenes.

**Goal:** support Mantaflow fluid/smoke/fire **while distributed**.

**Changes**
- Host bake-prepass → **OpenVDB sequence** (per-frame `.vdb`); transport **per-frame** (requires E-full per-frame addressing — without it every node needs the whole multi-GB cache and distribution collapses).
- Cloth / particle / soft body: prefer routing through **Alembic** (E) over native point caches for portability.
- Validator: accept fluid/cloth/etc. when a baked, frame-addressable, attached cache is present. **Note:** only `FLUID` has an existing baked-state check (`is_cache_baked_*`); `CLOTH`/`PARTICLE_SYSTEM` are blanket blocks with **no** baked-state scaffold — that scaffolding is net-new for those types.

**Distribution:** preserved **only** via per-frame cache addressing (E-full). **Risk:** high — cache volume, per-frame slicing & streaming, format/version portability, partial-transfer correctness, blob-storage pressure. Mitigations: per-frame addressing, hard cache-size caps, pinned Blender version, per-slice checksums. **Effort:** weeks–months.

**Test scenes:** Mantaflow FLIP/APIC fluid demo; Smoke/fire demo; `cloth_inner_springs.blend` → bake → Alembic.

**Acceptance:** baked sim renders perceptually-identical (within tolerance) to a single-machine reference, with per-frame cache transfer verified.

---

### Phase G — GN Simulation Zones, dynamic paint, rigid body *(high · last · demand-driven)*

**Goal:** close the remaining sequential effects.

**Changes:** each needs (a) detection from Phase A, and (b) a bake path: GN sim zone → bake GN cache / Alembic; rigid body → bake to keyframes or Alembic; dynamic paint → bake. Reuse E-full/F transport + per-frame addressing.

**Test scenes:** GN sim zone `2D_smoke_simulation.blend`; rigid-body / Cell Fracture; dynamic-paint scene; **stress:** Cosmos Laundromat (multi-feature final integration target).

**Acceptance:** each baked effect renders distributed-correct; the multi-feature stress scene completes end-to-end.

---

## 5. Cross-cutting engineering

- **Golden-file discipline.** The tolerance gate is `RenderGoldenFileAssert.AssertImageMatches` ([`Render.Tests/Utils/RenderGoldenFileAssert.cs`](../Render/OutWit.Controller.Render.Tests/Utils/RenderGoldenFileAssert.cs)) — **mean absolute per-channel RGB diff** vs `CYCLES_TOLERANCE=15.0` / `DEFAULT_TOLERANCE=5.0` (Eevee/GP), **not** byte equality. Goldens live at `@Prerequisites/render-golden/{testKey}_{engine}_{w}x{h}.png`; regenerate with `WIT_RENDER_UPDATE_GOLDENS=1`. (`RenderImageAnalysisStats` is only a diagnostic stat-bag — means/min/max/pixel counts — not the gate.) Every newly-admitted feature needs a golden-file test; budget for it explicitly.
- **Determinism / split-equivalence CI — net-new.** No multi-node test exists today: `RenderTestNodesManager` is hard-wired single-node. Add a check that renders the same frame on *N* simulated nodes and asserts equality within tolerance. Building blocks exist: the `IWitNodesManager` seam, the in-memory `RenderTestBlobService`, the `CalculateMeanAbsoluteRgbDifference` helper, and a **3-node mock to copy from the Grid test suite** (`Grid/OutWit.Controller.Grid.Tests/.../MockNodesManager.cs`). Distributed correctness *is* determinism; this catches silent per-node divergence.
- **Per-frame asset addressing.** Prerequisite for E-full / F. Extend **both** `RenderTaskData` and `RenderTaskBatchData` to carry per-frame attachment references so a node downloads only its frame's cache slice.
- **Blender version pinning.** Transported Alembic/VDB/point caches are version-sensitive. Record the bake's Blender version in the manifest and reject mismatches against the packaged node runtime.
- **Work split, restated.** *Controller* relaxes validation (A, C, E-lite) and, later, gains host-side bake/preflight activities (E-full/F/G). *Initiator* collects deps, packs, bakes, and populates the manifest (B, D, and the trigger side of F/G) — and for Blender most of D is already done. This keeps the node surface small and matches the user constraint that other subsystems stay mostly untouched: **A, C, E-lite are controller-only.**

---

## 6. Recommended sequencing & ROI

**This iteration: A → C → D-verify → E-lite.** All controller-side (except B, which is initiator and largely already built), all distribution-preserving, and together they cover the large majority of *attractive* non-sim and static-scatter scenes plus attached-Alembic content — which is exactly the "beautiful portal models get rejected" problem.

- **A** first — fix the oracle so the rest builds on honest classification, and so the portal UI gets actionable categories.
- **C** — highest visual payoff (rescues legacy scatter scenes), controller-only.
- **D-verify** — confirm the already-shipped Blender attachment path renders correct on ≥2 nodes; close UDIM / recursive-library / checksum gaps as needed.
- **E-lite** — remove the Alembic validator contradiction; immediate unlock for moderate caches.

**Defer E-full, F, G as demand-driven.** This is where cost explodes (cache data volume vs. the distribution model). For distributed rendering, the correct shape for "support simulations" is **funnel them through frame-addressable Alembic/VDB** (E-full/F) — bake once on the host, then split frames — rather than native sequential caches. Bias long-tail investment toward **Geometry Nodes + Alembic/VDB transport**, which is also where Blender's own physics roadmap is heading (legacy particle/hair physics is EOL).

---

## 7. Consolidated test-scene matrix

> Test fixtures for the controller, not public seed scenes. Confirm each scene's actual modifiers with the validator before relying on it. **Several fixtures already exist** under `@Data/` (gitignored): `cloth_inner_springs.blend`, `cloth_internal_air_pressure.blend`, `fluid-simulation_flip_vs_apic_solver.blend`, `lava_fluid-viscosity-demo.blend`, `greasepencil-bike.blend`, `UDIM_monster/`, `vse_media-transform/`. Download links in §7.1.

| Scene | License | Source | Feature exercised | Phase | Today | After phase |
|---|---|---|---|---|---|---|
| Classroom | CC0 | Blender demo (Christophe Seux) | clean / packed baseline | A,B (control) | pass | pass |
| BMW27 | CC0 | Blender demo (Mike Pan) | clean baseline | A,B (control) | pass | pass |
| Unpacked-texture variant | — | construct from above | external textures | B | block (if missing) | pass |
| Pabellon Barcelona | CC-BY | Blender demo (eMirage) | static particle scatter (pebbles) | C | **block** | pass |
| The Junk Shop | CC-BY | Blender demo (Alex Treviño) | hair-particle detailing (static) | C | **block** | pass |
| Fishy Cat | CC0 | Blender demo (Manu Jarvinen) | static fur | C | **block** | pass |
| Lone Monk | CC0 | Blender demo (Carlo Bergonzini) | scatter? (verify) | C | verify | pass if scatter |
| Spring | CC-BY-SA | Blender Studio | **dynamic** hair (neg. control) | C/G | block | block until baked |
| OpenVDB cloud scene | CC-BY | construct (Disney "Cloud" VDB) | external static volume | D | pass when attached | pass (verify) |
| Image-sequence texture | — | construct | external image sequence | D | pass when attached | pass (verify) |
| Linked-library scene | — | construct | linked `.blend` library | D | pass when attached | pass (verify) |
| Alembic cloth / character | — | construct (export `.abc`) | baked geometry cache | E-lite | **block** | pass |
| Mantaflow FLIP/APIC fluid | demo | Blender demo (S. Barschkis) | fluid sim → VDB | F | **block** | pass (baked) |
| Mantaflow smoke/fire | demo | Blender demo (Tornado smoke) | smoke/fire → VDB | F | **block** | pass (baked) |
| `cloth_inner_springs.blend` | demo | `download.blender.org/demo/` | cloth sim → Alembic | F | **block** | pass (baked) |
| `2D_smoke_simulation.blend` | demo | Blender demo (GN simulation) | GN simulation zone | A,G | pass (wrong!) → block | pass (baked) |
| Rigid-body / Cell Fracture | — | construct | rigid body → bake | A,G | pass (wrong!) → block | pass (baked) |
| Dynamic-paint scene | — | construct | dynamic paint → bake | A,G | pass (wrong!) → block | pass (baked) |
| Soft-body scene | — | construct | soft body → bake | A,G | pass (wrong!) → block | pass (baked) |
| Cosmos Laundromat (prod. benchmark) | CC-BY | Blender demo | multi-feature stress | G | block | pass (integration) |

> Rows marked "pass (wrong!) → block" are today's **silent failures**: they validate green but render incorrectly when split. Phase A converts them to honest blocks; Phase G makes them work via baking.

### 7.1 Where to download the examples

**Primary source — Blender Demo Files:** <https://www.blender.org/download/demo-files/> — current curated versions, each with its license and required Blender version (Classroom, Pabellon Barcelona, The Junk Shop, Fishy Cat, Lone Monk, and the Cosmos Laundromat production benchmark all live here, in the **Cycles** / **Rendering** sections).
*Note:* the `download.blender.org/demo/test/` and `/demo/cycles/` directory listings host **older** copies; prefer the demo-files page version where both exist. All direct links below are on `download.blender.org` and need no account.

**Direct downloads (verified):**

| Scene | License | Link |
|---|---|---|
| Lone Monk | CC0 | <https://download.blender.org/demo/cycles/lone-monk_cycles_and_exposure-node_demo.blend> |
| BMW27 | CC0 | <https://download.blender.org/demo/test/BMW27.blend.zip> |
| Classroom | CC0 | demo-files page (`#cycles`); legacy: <https://download.blender.org/demo/test/classroom.zip> |
| Pabellon Barcelona | CC-BY | demo-files page (Cycles section) |
| The Junk Shop | CC-BY | demo-files page (Rendering section) |
| Fishy Cat | CC0 | demo-files page (Cycles section) |
| Mantaflow FLIP/APIC fluid | demo | <https://download.blender.org/demo/physics/fluid-simulation_flip_vs_apic_solver.blend> |
| Lava (viscous fluid) | demo | <https://download.blender.org/demo/physics/lava_fluid-viscosity-demo.blend> |
| Cloth — internal springs | demo | <https://download.blender.org/demo/cloth_inner_springs.blend> |
| Cloth — air pressure | demo | <https://download.blender.org/demo/cloth_internal_air_pressure.blend> |
| GN Simulation Zone (2D smoke) | demo | <https://download.blender.org/demo/geometry-nodes/simulation/2D_smoke_simulation.blend> |
| Cosmos Laundromat (prod. benchmark) | CC-BY | demo-files page (Rendering section) |
| Spring (dynamic-hair control) | CC-BY-SA | 2.80 splash on demo-files page (Splash Screens); full assets on <https://studio.blender.org/> (subscription) |
| OpenVDB volume sample (Disney clouds) | CC-BY | <https://www.openvdb.org/download/> (Sample Models) |

**Constructed fixtures (no single download — build locally):**

- **Unpacked-texture variant** — open Classroom/BMW, *File → External Data → Unpack* (or repath textures to disk).
- **Image-sequence texture** — add an image sequence as a material texture in any scene.
- **Linked library** — *File → Link* an object/collection from a second `.blend`.
- **Alembic cloth/character** — bake a cloth/character sim, *File → Export → Alembic* (`.abc`), re-import via a Mesh Sequence Cache modifier.
- **Rigid body / dynamic paint** — set up the respective physics on a primitive scene; use the Cell Fracture add-on for fracture.

---

## 8. File reference (where each phase lands)

| Concern | File | Phase |
|---|---|---|
| Validation script generator (the "Python") | [`Render/OutWit.Controller.Render/Utils/BlenderValidationScript.cs`](../Render/OutWit.Controller.Render/Utils/BlenderValidationScript.cs) | A, C, E-lite |
| Blender process runner (writes temp `.py`, runs Blender, parses) | [`Render/OutWit.Controller.Render/Utils/BlenderRunner.cs`](../Render/OutWit.Controller.Render/Utils/BlenderRunner.cs) | A |
| Validation result model (add categories) | [`Render/OutWit.Controller.Render.Model/RenderValidateBlendData.cs`](../Render/OutWit.Controller.Render.Model/RenderValidateBlendData.cs) | A |
| Validate activity / adapter (`Render.ValidateBlend`) | [`Activities/WitActivityRenderValidateBlend.cs`](../Render/OutWit.Controller.Render/Activities/WitActivityRenderValidateBlend.cs), [`Adapters/WitActivityAdapterRenderValidateBlend.cs`](../Render/OutWit.Controller.Render/Adapters/WitActivityAdapterRenderValidateBlend.cs) | A |
| Attachment record (manifest entry) | [`Render/OutWit.Controller.Render.Model/RenderSceneAttachmentRefData.cs`](../Render/OutWit.Controller.Render.Model/RenderSceneAttachmentRefData.cs) | D, E-full |
| Transport hub (materialize + remap + write manifest) | [`Adapters/WitActivityAdapterRenderBuildBlendFromRefs.cs`](../Render/OutWit.Controller.Render/Adapters/WitActivityAdapterRenderBuildBlendFromRefs.cs) | D, E |
| Node-side path remap | [`Utils/BlenderSceneAttachmentRemapHelper.cs`](../Render/OutWit.Controller.Render/Utils/BlenderSceneAttachmentRemapHelper.cs) | D, E |
| Per-frame task models (add per-frame attachment refs) | [`Render/OutWit.Controller.Render.Model/RenderTaskData.cs`](../Render/OutWit.Controller.Render.Model/RenderTaskData.cs), `RenderTaskBatchData.cs` | E-full |
| Split activities (partition attachments per frame) | `Activities/WitActivityRenderSplit*.cs` + adapters | E-full |
| Host bake activity template to mirror | `WitActivityRenderBuildBlendFromRefs` (activity + adapter) | E-full |
| Golden-file gate | [`Render.Tests/Utils/RenderGoldenFileAssert.cs`](../Render/OutWit.Controller.Render.Tests/Utils/RenderGoldenFileAssert.cs) | all |
| Single-node test nodes manager (extend to N nodes) | `Render.Tests/.../RenderTestNodesManager.cs` (mirror Grid's `MockNodesManager.cs`) | all |
| Initiator collector (Blender — already complete) | `OmnibusCloud/Blender/.../bridge_scene_attachments.py` | B, D |
| Initiator collector (3ds-Max — only `ImageAsset`) | `OmnibusCloud/3ds-Max/.../MaxConnectedRenderSceneAttachmentService.cs` | D |

---

## Appendix — the rule, restated

A scene is distributable iff **every frame is independently computable** from the prepared `.blend`, its frame index, and per-frame assets transported to the node. Support is added by either **transporting** frame-independent data or **baking** sequential state into a frame-addressable cache and transporting that. The gate is the generated validation script (`BlenderValidationScript.BuildScript()`), which mirrors the controller's policy and — once Phase A lands — reports the sequential-state blind spots with actionable categories.
