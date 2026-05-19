# OutWit.Controller.Render.Dcc

Host-only upstream DCC build controller for OmnibusCloud. Accepts a neutral `DccScene` payload, validates the scene contract, generates a Blender Python scene script for the supported DCC subset, and invokes Blender headlessly to produce a `.blend` file blob. Stays separate from `OutWit.Controller.Render`, which owns the downstream `.blend` validation, preflight, rendering, collection, and encoding stages.

## Dependencies

- `OutWit.Controller.Variables` (version 1.0.0 or higher)
- `OutWit.Controller.Render` (version 1.15.0 or higher) — reuses the packaged Blender runtime from the Render controller for the current `.blend` generation path.

## Activities

| Activity | Side | Description |
|----------|------|-------------|
| `Render.BuildBlendFromDccScene` | Host | Validates `DccScene`, prepares a normalized build input, generates a Blender scene script, runs Blender headlessly, and returns the generated `.blend` blob. |
| `Render.ClearScene` | Host | Explicitly removes the source `DccScene` variable from the current pool after host-side build/preparation steps when the script author no longer needs it. |

## Variable Types

| Type | Description |
|------|-------------|
| `DccScene` | Inline neutral DCC scene payload transported through MemoryPack/WitRPC. |

## Supported DCC subset

The current `Render.BuildBlendFromDccScene` pass handles the following subset of the neutral DCC contract. Anything outside this list is either ignored or rejected at validation time.

### Geometry

- One mesh per scene (first mesh only).
- Per-triangle material indices for first multi-material assignment within that mesh.

### Cameras

- Single camera node.
- Animated FOV.
- Animated near/far clip distances.
- Combined transform + visibility animation slices.

### Lights

- Mesh / camera / light node transform animation (translation, rotation, scale).
- Visibility / renderability animation per node kind.
- Light intensity (scalar keyframes).
- Light color (color keyframes).
- Spot-cone angle.
- Light range.

### Materials

- Base color (constant + animated).
- Opacity (constant + animated).
- Metallic / roughness scalars (constant + animated).
- Normal-strength scalar (constant + animated).
- Alpha-mode policy: Blend / Clip / Hashed opacity workflows; alpha-clip threshold animation.

### Textures

- Base-color, opacity, metallic, roughness, normal texture slots (image attachments).
- UV scale / offset / rotation transforms.
- Animated UV scale / offset / rotation keyframes.

### Animation interpolation

- Bezier, Linear, Constant per-keyframe interpolation modes.
- All transform / visibility / scalar / color / UV animation slices flow through both `Render.Frame*` (frame sequences) and `Render.EncodeVideo` (video) downstream.

## Usage example

```
Job:DccBuild(DccScene:scene)
{
    Blob:blend = Render.BuildBlendFromDccScene(scene);
    Render.ClearScene(scene);
    ~ ... downstream: pass `blend` to Render.Frame / Render.Frames / Render.EncodeVideo ... ~
}
```

The activity returns a `Blob` reference to the generated `.blend`; subsequent rendering activities from the `Render` controller consume that blob directly.

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
