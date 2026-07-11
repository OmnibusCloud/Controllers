# Render.Dcc as a Universal Bridge — Status and Gaps

**Status date:** 2026-07-07 (open-beta start, Dcc 1.6.9 / Dcc.Model 1.5.6).
**Update 2026-07-11 (Dcc 1.6.18 / Dcc.Model 1.5.10 / Render.Model 1.7.0):** the bridge gained
the binary mesh sidecar (initiator #2 gets seconds-fast conversion for free — the sidecar is
generated server-side from the same contract), gzip-packed submission (`Render.UnzipDccScene`,
1 GB decompression cap), a 30-minute Blender build watchdog, and a fully VersionTolerant wire —
a reflection test in Dcc.Tests guards every type reachable from `DccSceneData`, so contract
fields can be appended without bricking older hosts (mind the one-time Render.Model 1.7.0
cutover: hosts ≥ v1.6.55 pair with initiators built against ≥ 1.7.0). An initiator-side lesson
worth stealing from the 3ds Max plugin: every approximation it makes is recorded per object and
surfaced as a named diagnostic — plan the same honesty channel into initiator #2 from day one.
The gaps below (AxisSystem ignored, etc.) remain accurate.
**Audience:** whoever builds the *second* scene initiator (Maya, Cinema 4D, Blender-as-source,
glTF import, …) or the *second* render target. Today the only initiator is the 3ds Max plugin
(`OmnibusCloud/3ds-Max`) and the only target is Blender/Cycles. The contract was kept clean of
Max types on purpose; this document records where universality is real, where it is untested,
and what a new initiator must know so nothing here gets rediscovered the hard way.

## 1. What is already source-agnostic (verified boundaries)

- **The wire contract (`OutWit.Controller.Render.Dcc.Model`) contains no 3ds Max types or
  constants.** All exposure/photometry calibration (e.g. the 3ds Max no-decay strength constant,
  displacement amount scaling, HDRI strength estimation) lives in the *plugin's* mapper, on the
  initiator side of the boundary. A new initiator brings its own calibration; the bridge does not
  compensate for source-renderer exposure models.
- **Units are honored**: `DccUnitSettingsData.UnitsPerMeter` drives a single `UnitsToMetersScale`
  through the whole build. Exporters declare their unit; nothing assumes centimeters.
- **Source-renderer emulation enters as neutral, optional flags** with renderer-independent
  definitions (see §3). Nothing in the generator asks "is this Max?"; it asks "does this material
  glow without lighting the scene?".
- **Cycles-specific implementation tricks are isolated** in the `DccBlender*` emitters
  (Light Falloff→Constant nodes for no-decay lights, Light Path mixes for camera-only emission,
  backfacing→transparent mixes). They are adapter details, not contract.

## 2. Known universality gaps (the checklist for initiator #2)

### 2.1 AxisSystem is declared but ignored (top gap)
`DccAxisSystemData` (handedness / up / forward) is validated and then **not used** by the
generator: Z-up right-handed is silently assumed, which happens to match both 3ds Max and
Blender. A Y-up source (Maya, glTF, Unity) must today pre-convert every transform, keyframe,
normal and deformation frame itself.
**Decision needed when it matters:** either honor `AxisSystem` in the generator (one root
conversion at build time) or document exporter-side normalization as the contract. Honoring it
server-side is the friendlier bridge; doing it half-and-half would be the worst outcome.

### 2.2 Shading-emulation flags are loose booleans
The current set: `NoDecay` (light does not attenuate with distance), `EmissionCameraOnly`
(glow that never lights the scene — no-GI source renderers), `BaseColorFromVertexColors` /
`EmissionFromVertexColors`, `IsBackdrop`, `BackfaceCull`, plus `RenderSettings.ViewTransform`
(OCIO name; scanline-class sources want `Standard`, PBR sources `AgX`).
Each is individually neutral (legacy Maya lights default to no decay too), but a new exporter has
to *know the combination* that reproduces its renderer. If a second initiator appears, consider
folding these into a declared source shading profile (e.g. `PhysicallyBased` vs
`LegacyNoGiScanline`) so combinations are named, not guessed.

### 2.3 Single render target by design
The generator emits Blender/Cycles Python only (EEVEE is partially covered where cheap, e.g.
`use_backface_culling`). The neutral model would survive a second target, but nothing enforces
that emitters stay behind an interface. If a second target ever appears, extract a target
abstraction *first*; do not fork the generator.

### 2.4 Global policies a new initiator inherits (deliberate, revisitable)
- **Motion-blur shutter opens AT the frame** (`motion_blur_position='START'`), not centered.
  Chosen because centered shutters look backwards across montage-cut keyframes (held CONSTANT)
  and ghost the previous shot. If a cinematic centered shutter is ever needed, make it a
  `RenderSettings` field rather than flipping the default.
- **Meshes are per-corner (face-varying) vertex streams**, and the generator WELDS by distance
  (threshold 1e-4 scene units) before Catmull-Clark subdivision and before displacement subsurf —
  unwelded corners shred both. Intentionally split vertices closer than the threshold would be
  merged; no corpus scene has hit this.
- **Frame semantics:** integer frames on the source's own timeline numbering; frame 0 is legal,
  negative frames are rejected. A source with a negative animation range must shift or clamp
  (3ds Max plugin currently clamps nothing — negative ranges are untested). `Fps` is honored
  (video timing and still-frame alignment depend on it — do not resample your timeline).
- **Keyframe interpolation is per-key** (`Bezier | Linear | Constant`). Teleporting keys
  (montage camera cuts) must be exported `Constant` or motion blur will sweep through the scene.
  Today the jump-detection heuristic lives in the 3ds Max mapper; it is a candidate to move
  server-side so every initiator gets it for free.

### 2.5 Contract quirks that will trip a new exporter (learned the hard way)
- **Sun lights must have `Range == 10`** (contract default) — the validator rejects anything else.
- **Texture slot semantics:** `Bump` carries grayscale *heights*; `Normal` carries normal
  *vectors*. Routing heights into `Normal` carves black craters. `NormalStrength` scales both.
- **Every texture slot / world environment image must resolve to an `AttachedFiles` entry** at
  build time (fail-fast validation); non-finite doubles are rejected at the contract level.
- **Wire format is MemoryPack `VersionTolerant`:** new members go at the tail with explicit
  `[MemoryPackOrder]`, and — the recurring trap — the hand-written `Clone()`/`Is()` on the model
  **must** include them, or the factory's scene clone silently drops the field before it reaches
  the generator. Deploy the server before any initiator starts sending new fields.
- **The farm caches activity results by input**: identical payloads return cached builds forever.
  Fine in production (real exports differ), but test harnesses must vary the payload (epsilon
  nudge) after a generator upgrade, or they will "verify" stale output.
- **Blender ≥ 4.4 slotted actions**: `Action.fcurves` is gone; any animation post-processing in
  generated Python must walk `layers/strips/channelbags` (see `action_fcurves` helper). A
  `hasattr` guard here silently disabled ALL keyframe interpolation for months.

### 2.6 Silent feature loss (process gap, not contract gap)
The bridge renders what it receives; dropped source features are the initiator's responsibility
to *warn about*. The 3ds Max exporter still drops some classes silently (3D-context procedural
maps rejected by the flat-bake guard, IES profiles, XRef textures). Rule for all initiators:
**every dropped feature must produce a summary warning** — a wrong-but-silent render is the most
expensive failure mode this project has had.

## 3. Quick reference: neutral flags and their intended semantics

| Field | Meaning (renderer-independent) | Set it when… |
|---|---|---|
| `DccLightData.NoDecay` | Illumination independent of distance | Source light has no/linear decay (legacy DCC default lights) |
| `DccMaterialData.EmissionCameraOnly` | Glows to camera/reflections, never lights the scene | Source renderer has no GI (scanline class) |
| `DccMaterialData.BaseColorFromVertexColors` | Base color = mesh corner color attribute | Vertex-color map wired into diffuse |
| `DccMaterialData.EmissionFromVertexColors` | Emission color = mesh corner color attribute | Vertex-color map wired into self-illumination |
| `DccNodeData.IsBackdrop` | Scenery: visible to camera, never lights/shadows | Sky domes, matte backdrops |
| `DccMaterialData.BackfaceCull` | Single-sided surface | Source renderer culls backfaces and scene relies on it |
| `RenderSettings.ViewTransform` | OCIO view transform | `Standard` for clamped-sRGB sources, `AgX` for PBR |

## 4. Suggested order of work when initiator #2 starts

1. Decide and implement the `AxisSystem` policy (§2.1) — everything else builds on transforms.
2. Name the shading profiles (§2.2) instead of documenting boolean recipes.
3. Move teleport-cut detection server-side (§2.4) so cuts work for every source.
4. Add a "dropped features" warning channel to the submission summary contract (§2.6).
