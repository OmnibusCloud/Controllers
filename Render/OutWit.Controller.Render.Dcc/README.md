# OutWit.Controller.Render.Dcc

Host-only upstream DCC build controller for WitCloud.

Dependency:
- requires `OutWit.Controller.Render` because the current `.blend` generation path reuses the packaged Blender runtime from the Render controller.

Current scope:
- accepts `DccScene` inputs;
- validates the neutral DCC scene contract;
- prepares a normalized internal build input for the future `.blend` generation step;
- generates a first-pass Blender Python scene script for the supported DCC subset;
- invokes Blender headlessly to generate a `.blend` file for the currently supported subset;
- currently supports the first mesh, camera, light, and basic material/image-attachment subset, including base-color, opacity, metallic, roughness, and normal texture slots, basic UV scale/offset/rotation transforms, normal strength, first alpha-mode policy support for blend/clip/hashed opacity workflows, first multi-material mesh assignment via per-triangle material indices, a first transform-animation slice through mesh/camera/light node keyframes with Bezier/Linear interpolation support that already flows through frames and video rendering for all three node kinds, a first visibility/renderability animation slice through mesh/camera/light node visibility keyframes with Bezier/Constant/Linear interpolation support that already flows through frames and video rendering for all three node kinds, first material-base-color/material-alpha-clip-threshold/material-opacity/material-metallic/material-roughness/material-normal-strength plus first texture UV-transform animation slices through color/scalar alpha-clip-threshold/opacity/metallic/roughness/normal-strength and UV scale/offset/rotation keyframes, camera-FOV, and camera-clip animation slices plus first light-intensity, light-color, spot-angle, and range animation slices through scalar intensity keyframes, color keyframes, spot-angle keyframes, and range keyframes with interpolation support that already flow through frames and video rendering within supported kinds, and combined transform-plus-visibility animation scenarios with explicit interpolation coverage that already flow through frames and video rendering for mesh/camera/light node kinds;
- stays separate from `OutWit.Controller.Render`, which continues to own `.blend` validation, preflight, rendering, collection, and encoding.

## Activities

| Activity | Side | Description |
|----------|------|-------------|
| `Render.BuildBlendFromDccScene` | Host | Validates `DccScene`, prepares a normalized build input, generates a Blender scene script, runs Blender headlessly, and returns the generated `.blend` blob. |
| `Render.ClearScene` | Host | Explicitly removes the source `DccScene` variable from the current pool after host-side build/preparation steps when the script author no longer needs it. |

## Variable Types

| Type | Description |
|------|-------------|
| `DccScene` | Inline neutral DCC scene payload transported through MemoryPack/WitRPC. |
