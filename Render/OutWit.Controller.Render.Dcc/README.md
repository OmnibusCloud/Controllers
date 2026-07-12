# OutWit.Controller.Render.Dcc

Host-only upstream DCC build controller for OmnibusCloud. Accepts a neutral `DccScene` payload, validates the scene contract, generates a Blender Python scene script (with a binary mesh sidecar for bulk data), and invokes Blender headlessly to produce a `.blend` file blob. Stays separate from `OutWit.Controller.Render`, which owns the downstream `.blend` validation, preflight, rendering, collection, and encoding stages.

## Dependencies

- `OutWit.Controller.Variables` (version 1.0.0 or higher)
- `OutWit.Controller.Render` (version 1.23.0 or higher) — reuses the packaged Blender runtime from the Render controller for the `.blend` generation path.
- `OutWit.Controller.Render.Model` (version 1.7.0 or higher) — **wire cutover note**: 1.7.0 flipped the scene-attachment type to MemoryPack VersionTolerant; payloads written against older `Render.Model` cannot be read by 1.6.18+ hosts and vice versa. Deploy hosts and initiators across that boundary together.

## Activities

| Activity | Side | Description |
|----------|------|-------------|
| `Render.BuildBlendFromDccScene` | Host | Validates `DccScene`, prepares a normalized build input, materializes blob-backed attachments, generates a Blender scene script + binary sidecar, runs Blender headlessly (packing all images into the file), and returns the generated `.blend` blob. |
| `Render.UnzipDccScene` | Host | Decompresses a gzip-packed MemoryPack scene payload (the `*Packed` job scripts' input — payloads compress 6–10×). Decompression is capped at 1 GB. |
| `Render.ClearScene` | Host | Explicitly removes the source `DccScene` variable from the current pool after host-side build/preparation steps when the script author no longer needs it. |

## Variable Types

| Type | Description |
|------|-------------|
| `DccScene` | Inline neutral DCC scene payload transported through MemoryPack/WitRPC (VersionTolerant end to end — a reflection test guards that every nested type stays tolerant). |

## Supported DCC subset

The `Render.BuildBlendFromDccScene` pass handles the following subset of the neutral DCC contract. Anything outside this list is rejected at validation time with a named contract error — the layer's design rule is *fail fast and readable*, never build a silently wrong scene.

### Geometry

- Any number of meshes; per-node transforms with keyframes.
- Per-triangle scene-level material indices (multi-material) or a per-node material binding.
- Primary + secondary UV layers, per-corner vertex colors.
- Per-frame vertex deformation (shape-key sequences) for skinned/simulated meshes.
- Render-time subdivision levels (Subsurf, clamped 0–6) and displacement with surface-relative height semantics.
- Mesh bulk data (positions/normals/UVs/colors/indices/shape keys) travels in a **binary sidecar** the generated script reads back through numpy `foreach_set` — the conversion of a heavy scene runs in seconds, not tens of minutes.

### Cameras

- Multiple cameras with an active-camera selection; FOV, near/far clip, and depth-of-field; transform and property keyframes.

### Lights

- Point / spot / area / sun kinds with color, intensity, range, spot cone + blend, area sizes, cast-shadow flags.
- 3ds Max "no decay" lights get a constant-falloff emulation node; photometric-unit normalization happens initiator-side.
- Intensity/color/range/cone animation.

### Materials

- Principled surface: base color, metallic, roughness, IOR, transmission, emission, opacity with Blend/Clip/Hashed alpha modes, normal strength.
- Texture slots: base color, roughness, metallic, normal/bump, opacity, displacement — with authored UV tiling/offset (and their animation).
- Scalar/color/UV animation on all of the above; per-keyframe Bezier/Linear/Constant interpolation.

### World / environment

- Environment color or image (HDRI) with authored rotation; screen-mapped backdrop mode (visible to camera and in reflections); environment-as-light-source flag that suppresses the default light rig.

### Attachments

- Blob-backed image attachments are materialized into the build sandbox (path-traversal guarded) and **packed into the `.blend`** (`pack_all`), so the artifact is self-contained.
- `bpy.data.images.load` is only ever handed a **materialized attachment path** inside the sandbox. A scene's raw client texture paths (`SourcePath`/`RelativePath`) are untrusted and never reach a load; referenced images are validated to have an attachment, and an image asset without one — always unreferenced — is skipped rather than loaded, so a crafted scene cannot read an arbitrary host file (or open an outbound UNC connection) via `pack_all`.

## Operational bounds

- `Render.UnzipDccScene` caps decompression at **1 GB** (gzip-bomb guard; ~6× the heaviest verified scene).
- The Blender build carries a **30-minute wall-clock watchdog** on top of job cancellation — a wedged process cannot hold a worker slot indefinitely.
- The build sandbox is a per-job temp directory, cleaned on success and failure.

## Usage example

```
Job:RenderDccSceneStillPacked(ByteCollection:packedScene, Variable:frame, RenderOptions:options)
{
    DccScene:scene = Render.UnzipDccScene(packedScene);
    Blob:blend = Render.BuildBlendFromDccScene(scene);
    Render.ClearScene(scene);
    ~ ... downstream: pass `blend` to Render.Frame / Render.Frames / Render.EncodeVideo ... ~
}
```

The bundled `RenderDccScene*Packed` scripts (still / tiled / frames / video / **export-blend**) ship in `OutWit.Controller.Render.Dcc.Scripts`; the export variant returns the built `.blend` itself as the job result.

## Project structure

```
OutWit.Controller.Render.Dcc/
  Activities/          - WitActivityRenderBuildBlendFromDccScene, WitActivityRenderClearScene
  Adapters/            - matching adapters (host-only execution)
  Variables/           - WitVariableDccScene wrapper
  Services/            - DCC-side pipeline: validator, build-input factory, blend-file builder,
                       Blender-binary resolver, Blender Python script generator
  Models/Build/        - internal build-time DTOs (DccBlendBuildArtifact, DccSceneBuildInput)
  Properties/          - AssemblyInfo
  build/               - consumer-side MSBuild .targets shipped inside the nupkg
  WitControllerRenderDccModule.cs - plugin entry point (DI registrations)

OutWit.Controller.Render.Dcc.Model/
  Scene/, Geometry/, Cameras/, Lights/, Materials/, Textures/,
  Animation/, Metadata/, Values/     - shared DCC types split by domain
```

The companion `OutWit.Controller.Render.Dcc.Model` ships separately on NuGet so external tooling can reference the neutral DCC scene types (`DccSceneData`, `DccMeshData`, `DccCameraData`, etc.) without taking the host-only Dcc controller as a runtime dep.

## Companion controller

See [`OutWit.Controller.Render`](../OutWit.Controller.Render/README.md) for the downstream pipeline that consumes the generated `.blend` blob: frame rendering, video encoding, tiled stills, preflight validation, runtime diagnostics.
