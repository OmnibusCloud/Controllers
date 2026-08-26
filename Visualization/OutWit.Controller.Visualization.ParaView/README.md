# OutWit.Controller.Visualization.ParaView

Headless scientific visualization for OmnibusCloud: safely executes **OmnibusCloud ParaView packages**
(a saved `.pvsm` state plus content-addressed data attachments) and distributes independent rendering
tasks across worker nodes. The companion
[`OutWit.Controller.Visualization.ParaView.Model`](../OutWit.Controller.Visualization.ParaView.Model/README.md)
carries the shared data types and the `paraview.*@1` job document vocabulary for non-.NET initiators
(the ParaView GUI plugin).

**Status: in development.** The controller shape, validation, splitting, the node runner contract, the
test harness, the per-platform ParaView runtime assets (`paraview-v0.1.0`) and the bundled OmnibusCloud
`.frd` reader are complete; distributed animation at scale and the platform completion pass follow.

## Activities

| Activity | Side | Purpose |
|---|---|---|
| `ParaView.Validate(ParaViewSceneRef scene, ParaViewOutputOptions options)` → `ParaViewValidationReport` | host | Treats the package as untrusted input: package reference, attachments and logical paths, runtime requirement (exact ParaView major.minor, allowlisted plugins only), output options and limits, then the state — hardened XML parse (no DTD/entities, depth/count/size bounds), proxy allowlist, programmable-pipeline rejection, file references against the package, views, timeline, frame selection. Downloads only the state. An invalid package is a *completed* activity with an invalid report. |
| `ParaView.Split(scene, report, options)` → `ParaViewRenderTaskCollection` | host | Deterministic tasks — one per resolved timestep of the resolved view; identity = package digest + dataset identity (reserved, empty) + view + timestep + options digest; **each task carries only the attachments its timestep needs** (series pieces by `TimestepIndices`, plus statics and series indexes). Refuses an invalid report. |
| `ParaView.RenderFrame(ParaViewRenderTask task)` → `ParaViewRenderResult` | node | Materializes the state and the task's subset into an isolated, task-unique package root (digests verified while copying, nothing outside the subset requested), writes the controller-owned runner, runs `pvpython` under an allowlisted environment, interprets exit code + status document, validates the output (signature, dimensions, alpha, no stray files), publishes it. Cancellation and the wall-clock limit kill the whole process tree; the workspace is deleted on every path. |
| `ParaView.SplitBatched(scene, report, options)` → `ParaViewRenderTaskBatchCollection` | host | **0.5.0 (FrameBatch).** The tasks of `Split` grouped into consecutive chunks (`Tasks/ParaViewChunkPolicy`: `clamp(ceil(outputs / 24), 1, 32)` outputs per chunk — a small job still splits per output, a long animation batches up to 32 per process; a chunk also closes early when the next output would push its attachment union over the per-task byte limit). A chunk hoists the state, options and runtime, carries the **union** of its members' subsets in package order (statics, indexes and series anchors once per chunk instead of once per output), and lists its members with their global task indices. |
| `ParaView.RenderFrameBatch(ParaViewRenderTaskBatch batch)` → `ParaViewRenderResultBatch` | node | **0.5.0.** One workspace, one materialization of the union, ONE `pvpython` process for the whole chunk: the state loads and validates once, every output then selects its timestep, moves the camera from the state's captured framing (restored between outputs — the moves are absolute, never cumulative), renders to its own file and verifies it; the controller validates the output directory against the exact expected set, publishes one blob per output and returns the per-output results in a wrapper. All-or-nothing: one failed output fails the chunk and nothing of it is published; an EGL crash demotes the node and retries the whole chunk on OSMesa from a clean slate. Process startup (~2.5 s of a ~3 s single-frame cycle) is paid once per chunk — measured on the dev box: 5 corpus frames 2.15 s in one process vs 10.25 s in five (4.8×). |
| `ParaView.Compose(ParaViewDataScene data, ParaViewOutputOptions options)` → `ParaViewSceneRef` | node | **0.3.0.** Composes a scene from BARE data — one blob-referenced CalculiX `.frd` plus the presentation choices of a data scene (colour array, colour-map preset, representation, scalar bar, camera direction, fit) — into a REAL saved state: materializes the data, runs the controller-owned composer (`compose_scene.py`: bundled reader → representation → colouring with one baked colour range → camera fitted to the union of the data bounds → `SaveState`, absolute data path rewritten to the logical path), hashes and publishes the state, stamps the data digest, and returns an ordinary package reference — after running it through the host validator itself, so a state the allowlist would refuse never leaves the node. One task per job through `Grid.Delegate()`. |
| `ParaView.Collect(rendered, options)` → `BlobCollection` | host | Accepts the per-frame OR the batch result collection (`Tasks/ParaViewResultFlattener`), restores task order, fails on missing/duplicate/conflicting identities. |
| `ParaView.CollectStill(rendered, options)` → `Blob` | host | Either shape; exactly one result → one image blob. |

## Rendering backend (GPU/EGL on Linux)

Windows and macOS render through pvpython's platform default (hardware OpenGL where a driver exists).
On headless Linux the runner historically pinned OSMesa (bundled software GL, works on any node); the
bundled runtime also carries `vtkEGLRenderWindow`, so `ParaViewRenderingBackend` probes ONCE per node
process: a trivial render with `VTK_DEFAULT_OPENGL_WINDOW=vtkEGLRenderWindow` must come back with an
EGL window AND a real hardware OpenGL renderer string (Mesa's EGL silently lands on llvmpipe — a
GPU-looking window over a software rasterizer is rejected). Any crash or software verdict falls back
to the certified OSMesa path, so no node is ever excluded. Tasks and the benchmark share the decision,
so the measured rate always reflects the backend tasks actually use. Operations override:
`OUTWIT_PVPYTHON_OPENGL_WINDOW=<window class>` pins the choice and skips probing. A Linux node wanting
GPU rendering needs the driver's EGL stack (GLVND `libegl1` + the vendor library the driver installs).

## Node benchmark and work distribution

The two distributed activities carry measured node benchmarks (`Runtime/ParaViewBenchmark` + the
embedded `Runner/benchmark_frames.py`); the host-side ones answer the engine's benchmark pass with the
default rate. At startup every worker runs the pass: a benchmark iteration is a **complete task
cycle** — a fresh pvpython process that builds a procedural Wavelet scene (61³ points contoured at
four values, clipped and sliced) and renders 1920×1080 PNG frame(s) while rotating the camera. **Every
frame re-executes the contour+clip pipeline** (one isosurface value alternates between two fixed
levels) — the cost a real task pays; without it VTK's filter caching leaves only rasterization +
readback in the loop and a 32-core node measures nearly the same as a 2-core one. `SaveScreenshot` is
included, so readback and encoding count. One warm-up cycle (a cold page cache costs ~3×), then timed
cycles until `MinDuration` (5 s fallback), at least 2 and at most 8; a cycle is killed at 5 minutes.
The result is `paraview-pixels@v1`: **output pixels per second of complete cycles**, with
`render-window`/`render-device` (`vtkOSOpenGLRenderWindow` = software), `task-cycles`,
`cycle-seconds`, `render-seconds` (the in-process render share), `frames-per-cycle`,
`paraview-version` and `scene-points` in `Custom`. A node without a usable runtime reports rate 0.

Two shapes, two datasets, so the allocator never mixes them: `ParaView.RenderFrame` measures ONE frame
per process (`paraview-benchmark-wavelet@v3`) — a small-frame task is startup-dominated (~2.5 s of a
~3 s cycle), which a steady-state render loop (the v2 dataset) never saw, overrating GPU nodes;
`ParaView.RenderFrameBatch` measures 8 frames per process (`paraview-benchmark-wavelet@v4-batch`), the
shape a chunk runs in. Dev-box figures (RTX, Windows): 0.96 M px/s single-frame (cycle 2.15 s) vs
5.55 M px/s batched (cycle 2.99 s for 8 frames, 1.95 s of it rendering).

Determinism: the scene, the camera step and the two alternating isosurface sets are fixed, so every
node times the same frames (per-frame spread across a full rotation measured within ±10%; the two
isosurface sets differ by under 1% of workload). The work estimate of a task (or a chunk: the sum of
its outputs' pixels plus the union's bytes) is expressed in the same unit — `pixels + materializedBytes / 64`
— so the Grid allocator (`WitGridTaskAllocator`: longest-processing-time first, rate-weighted, fewer
nodes when the makespan does not suffer) hands a GPU workstation proportionally more work than a
software-GL VM.

## Bundled scripts (`OutWit.Controller.Visualization.ParaView.Scripts`)

```
RenderParaViewFrames(ParaViewSceneRef:scene, ParaViewOutputOptions:options)            -> BlobCollection:result
RenderParaViewStill (ParaViewSceneRef:scene, ParaViewOutputOptions:options)            -> Blob:result
RenderParaViewVideo (ParaViewSceneRef:scene, ParaViewOutputOptions:options, VideoOptions:video) -> Blob:result   (Render.EncodeVideo; requires the Render controller)
ValidateParaViewScene(ParaViewSceneRef:scene, ParaViewOutputOptions:options)           -> ParaViewValidationReport:result

RenderParaViewDataFrames(ParaViewDataScene:data, ParaViewOutputOptions:options)        -> BlobCollection:result   (0.2.0 of the scripts)
RenderParaViewDataStill (ParaViewDataScene:data, ParaViewOutputOptions:options)        -> Blob:result
RenderParaViewDataVideo (ParaViewDataScene:data, ParaViewOutputOptions:options, VideoOptions:video) -> Blob:result
ValidateParaViewData    (ParaViewDataScene:data, ParaViewOutputOptions:options)        -> ParaViewValidationReport:result
```

From scripts 0.3.0 (controller 0.5.0) the four animation scripts (`RenderParaView[Data]Frames/Video`)
run the **FrameBatch** chain — `ParaView.SplitBatched` → `Grid.ForEach(task in tasks) =>
ParaView.RenderFrameBatch(task)` → `ParaView.Collect` — and the still scripts stay on
`Split`/`RenderFrame` (one output, nothing to amortize). Their signatures and results are unchanged;
callers (the GUI plugin, WitSweep) need nothing.

### Composed scenes — bare data in, the same chain out (controller 0.3.0)

The `*Data*` scripts take `paraview.dataScene@1` instead of a package: one attachment (a CalculiX
`.frd` result already in blob storage — the sweep's `FrdBlobId`, no client upload) plus bounded
presentation choices (`colorArrayName`, `colorAssociation`, `colorComponent`, `colormapPreset` from an
allowlist, `representation`, `showScalarBar`, `cameraDirection`, `fitTo`). Their first line is
`ParaViewSceneRef:scene = Grid.Delegate() => ParaView.Compose(data, options)`: ONE node task composes
the state (the Blender-bake shape), and from there the chain is byte-for-byte the plain one —
`Validate` validates a real `.pvsm` with the same allowlist, `Split` fans out by timestep × orbit
frame, `RenderFrame` renders, `Collect` orders. The plugin's `RenderParaView*` scripts are untouched:
no delegate, no compose, no extra task. The camera and the colour range are baked into the state,
so every frame shares the framing; the timeline comes from the reader (the state's TimeKeeper);
a colour array the data does not carry fails the job naming the arrays that exist; with no array
named, the composer colours by the first RESULT array (point arrays first, then cell arrays) and
never by the reader's bookkeeping arrays — `NodeNumber`, `ElementNumber`, `ElementType`,
`ElementGroup`, `Material` (0.4.2; before that a bare heat run came out coloured by node number). The composer
never creates a proxy outside the allowlist — and if it ever did, the node's own validator pass
refuses the state before it is published. Cost: one compose cycle (seconds) before the fan-out.

### Camera moves — turntable, rise, spiral, approach, rock (controller 0.4.0)

The `paraview.turntable@1` document grew three members in Model 0.4.0 (docs 06, part B):
`elevationDegrees` (total rise about the camera's right axis, ±170°), `dollyFactor` (end distance
to the focal point relative to the captured one, 0.05..20) and `oscillate` (sway back and forth
around the captured framing instead of progressing to the full move). Azimuth progresses as
`i / N` (cyclic — a 360° orbit loops), elevation and dolly as `i / (N - 1)` (the last output reaches
the full move); oscillating moves follow `sin(2πi/N) / 2` of every total. A client's presets are
combinations: orbit = degrees only; rise = elevation only; spiral = both; approach = dolly only;
rock = degrees + oscillate. The node applies azimuth (as below), then a rigid rotation of position
and view-up about the camera's right axis (`camera_elevation`), then the distance factor
(`camera_dolly`, parallel scale scaled alike). Task identities carry the full transform AND the
orbit axis / time mode, and every output of one timestep has its own file name
(`frame_<timestep>_<orbit>`), so the outputs of two different moves never collide or overwrite.

#### The original turntable semantics (controller 0.2.0)

`ParaViewOutputOptions.turntable` (`paraview.turntable@1`: `frames`, `degrees`, `timeMode`,
`axis`) turns the frames and video scripts into a camera orbit without any animation track in the
state: Split emits one task per orbit output and the node revolves the state's camera about the
orbit axis through the focal point by `degrees * i / frames` before rendering (`camera_azimuth` /
`camera_axis` in the task file; about the camera's view-up this is `vtkCamera.Azimuth`, about a world
axis the camera is rotated rigidly — position and view-up — so a tilted camera keeps its tilt and a
camera looking straight down the axis rolls instead of degenerating).
`Fixed` gives every selected timestep a full orbit (one timestep in, a showcase orbit out); `Advancing`
renders exactly `frames` outputs with the data time spread from the first selected timestep to the
last. Outputs count against the same per-job limit; the validation report's `outputCount` tells the
initiator how many frames the job will render. Task identities of orbit outputs carry the orbit
position and azimuth, so they never collide with the plain task of the same timestep.

### Frame indices from the end (controller 0.4.1)

A frame selection's indices may be negative, Python style: `-1` is the last timestep of the resolved
timeline, `-count` the first (`Single.First`, `Range.First`/`Last`, `Explicit.Indices` alike). A
client that does not know how many timesteps the data carries — WitSweep rendering a variant's
`.frd` by blob reference through a composed scene — asks for the last one with `First = -1` and no
round trip; an index below `-count` is still "outside the timeline", and an explicit list that names
one timestep twice through both forms is still a repeat. The document does not change (the members
were `int` already); a 0.4.0 host rejects a negative index as before.

### Hardening after the 2026-08-22 audit (controller 0.4.3)

- **C-H1** `LoadMaterials` (on the allowlisted `materials/MaterialLibrary`, which lives outside the
  file-reference groups) is a blocked property: a non-empty value is refused by the host and by the
  runner's pre- and post-load scans like a script property; the empty value every saved state carries
  stays inert. The corpus guard asserts no allowlisted proxy outside the file-reference groups carries
  a path-valued property.
- **C-M1** the EGL demote-and-retry acts only on a CRASH — the runner died without writing its status
  document (`ParaViewRunnerCrashedException`). A policy refusal, a usage error or the wall-clock limit
  is the task's own verdict and is never retried; the same rule governs `Compose`.
- **C-M2** the retry starts from a clean slate (`ParaViewTaskWorkspace.ClearAttemptArtifacts`: status
  document + every output of the crashed attempt), so a second attempt's error can never blame the first.
- **C-M8** a crash outranks a pinned `OUTWIT_PVPYTHON_OPENGL_WINDOW`: once demoted, the node stays on
  software for the rest of the process even with the pin.
- **C-H2** off Windows (no kill-on-close job object) `render_task.py` and `compose_scene.py` run a
  parent watchdog thread: when the controller process is gone (`getppid()` changed) the runner leaves
  through `os._exit` at once instead of pinning the node until the wall-clock limit.
- **P-H5 / P-H6 (reader 1.0.1, controller 0.4.4)** the bundled `.frd` reader refuses a node, element
  or result block that ends without its `-3` terminator and a `-1` record cut mid-line (a truncated
  file was a silently truncated mesh with NaN coordinates and exit 0), and a malformed numeric field
  surfaces as an `FrdFormatError` with a byte position instead of a raw `ValueError`.
- **C-M5 (controller 0.4.5)** node-side materialization fails closed on a corrupt declaration: a
  negative size or a non-empty malformed SHA-256 refuses the attachment (an EMPTY digest / zero size
  is the compose contract's "the node stamps it" and stays allowed). Wave 2 coverage:
  `ParaViewResultOrdering` (frame-set holes, duplicates, empty images, duplicate identities),
  `ProcessTreeGuard` (a child assigned to the job dies when the job closes), the workspace's
  fail-closed branches, and a host↔runner parity table that runs one list of logical paths through
  `ParaViewLogicalPath.Check` and `render_task.check_logical_path` (a python on PATH) — the two
  hand-mirrored rule sets must agree on every verdict. `InternalsVisibleTo` opens the internal
  seams to the test project as in the Render family.

## The runner contract

The node never builds a shell string. It invokes the bundled `pvpython` directly:

```
pvpython --force-offscreen-rendering --disable-registry <work>/runner/render_task.py --task-file <work>/task.json
```

with cwd = the task's package root and an environment built from an allowlist
(`PATH` = the runtime's bin directory + system dirs, task-private `HOME`/`APPDATA`/`TEMP`,
`PYTHONNOUSERSITE=1`, no `DISPLAY`, no plugin paths; on Linux `VTK_DEFAULT_OPENGL_WINDOW=vtkOSOpenGLRenderWindow`
selects the software-rendering baseline). `task.json` (schema 2 since controller 0.5.0) carries the
shared inputs once — the state path, the package root, the status path, view, size, format, the
optional bundled-reader path, the effective proxy allowlist and the blocked proxy/property lists — and
the `outputs` to render in order, each with its file, timestep and camera move: one for
`ParaView.RenderFrame`, the chunk for `ParaView.RenderFrameBatch` (`Runtime/ParaViewRunnerTask.cs`,
`ParaViewRunnerOutput.cs`). The state loads and validates once per process; every output selects,
moves the camera from the captured framing (restored between outputs), renders and verifies. The
runner writes `status.json` on every exit path (`Runtime/ParaViewRunnerStatus.cs`; schema 2 adds the
per-output verdicts) and exits non-zero on any discrepancy — one failed output fails the whole process.
A single-valued file reference of the state must be a materialized package file; a file series (`FileNames`
with several elements) legitimately lists every file while the task carries only its own piece, so at least
one must exist — and any VTK error during load or render (a reader touching a file this task did not
materialize, a GL failure) fails the task through an error observer rather than producing a blank frame.
File-series readers (PVD, `FileNames` lists) open the FIRST file of a series when the state loads, so every
task carries that *series anchor* next to the index and its own piece (`ValidationReport.SeriesAnchors`).
The runner (`Runner/render_task.py`), the proxy allowlist (`Allowlists/paraview-<major.minor>.json`) and
the bundled reader are **embedded in the controller assembly** and written per task into the task's own
work directory — the only plugin path the runner consults — so a node can never run a stale or foreign copy.

`OUTWIT_PVPYTHON` overrides the runtime location (operator escape hatch and the test seam).

## The bundled reader (`Plugins/omnibuscloud_frd_reader.py`)

CalculiX results come as `.frd` files, which ParaView cannot open by itself. The controller bundles a
single-file `VTKPythonAlgorithm` reader — plugin name `OmnibusCloudFrdReader`, version `__version__`
(1.0.0) — as an embedded resource; the GUI plugin ships the byte-identical file, so the desktop and the
render nodes read a package with the same code. A package that uses it declares
`runtime.plugins = [{ name: "OmnibusCloudFrdReader", version: "1.0.0" }]`; the validator admits the
`sources/OmnibusCloudFrdReader` proxy only for such packages (`pluginProxies` of the allowlist), checks
the version against the bundled reader (same major, greater-or-equal minor), and the executor writes the
reader into the task's plugin directory — the only plugin path the runner loads.

What it reads: the cgx "Result Format" as ccx writes it — ASCII short or long records (binary variants
are rejected with a clear error; ccx never writes them); nodes, elements and nodal results
(`100C`/`100CL` blocks) as point arrays named after the dataset (`DISP`, `STRESS`, `NDTEMP`, …; a
vector keeps 3 components, a tensor 6, components flagged as cgx-computed such as `ALL` are skipped,
nodes a block does not mention get NaN), `NodeNumber` on points and `ElementNumber` / `ElementType` /
`ElementGroup` / `Material` on cells, `StepNumber` / `StepValue` / `AnalysisType` as field data. Every
result step is a time step: the step value (time, frequency, load factor) when values strictly increase,
otherwise the 1-based ordinal (degenerate modes at one frequency, static steps all at 1.0). Element
types follow the cgx manual's numbering: he8, pe6, tet4, he20, pe15, tet10, tr3, tr6, qu4, qu8, be2, be3
→ the VTK linear/quadratic cells, with the he20 and pe15 mid-side nodes reordered (cgx lists vertical
mid-edges before top ones; VTK the other way round) and **no** wedge corner swap (cgx's base triangle is
the orientation of VTK's wedge parametric map and of VTK's own CGNS reader). That mapping is proven on
real ccx output, not read off a diagram: `RuntimeTools/generate_frd_fixtures.py` runs CalculiX on one
element of every type (solids, shells and beams in both expanded and 2D output, a transient heat
transfer), and `RuntimeTools/check_frd_reader.py` asserts under pvpython that every cell validates,
every quadratic mid-side node is its VTK edge midpoint, every 3D cell's parametric Jacobian is
positive, arrays have their shapes and every step has data — the real-runtime suite runs the same check.

## Runtime

The pinned runtime is **ParaView 6.1.1** (`Runtime/ParaViewRuntimeInfo.cs`). Version 1 requires an exact
major.minor match with the producing ParaView; patch mismatch is tolerated. Since ParaView 6.0 a single
binary serves every rendering backend — OSMesa software rendering is the certified baseline (Linux), EGL/GPU
stays opportunistic. The per-platform runtimes are the official Kitware binaries trimmed to the headless
pvpython (`RuntimeTools/trim_paraview.py`; Linux/macOS symlink chains collapsed to one loader-named file
each by `collapse_symlinks.py`, because zip archives and the engine's extractor carry no symlinks), one zip
per platform declared as `<ControllerDataAsset>` on the `paraview-v<ver>` GitHub Release (author-packed with
`outwit-assets-pack`) and extracted to `paraview.module/paraview/<platform>/`.

What a trimmed runtime keeps: pvpython (+ pvbatch), every library in the static import closure of
pvpython and the Python extension modules, the Python standard library and numpy, the ParaView Python
packages, OSPRay's CPU device (ParaView initialises OSPRay with every render view), the bundled Mesa
(Linux), and the ParaView license / SPDX materials. What goes: the Qt client and its libraries, NVIDIA
IndeX / MDL / OptiX / VisRTX, MKL, Catalyst stubs, ParaView's own plugins, docs, examples, translations,
web/, and the Python packages pvpython does not need for rendering (scipy, pandas, matplotlib, sympy,
h5py, netCDF4, mpi4py, PIL, …). The official 1.4 GB Windows zip becomes ~700 MB, the 2.8 GB Linux tarball
~1.35 GB (the flat `lib/` is one DT_NEEDED chain from `libvtkRemotingApplication` and cannot shrink
without rebuilding ParaView). Every trim is verified by the real-runtime tests and by regenerating the
allowlist from the trimmed runtime — never by the scripts alone.

| Platform | Archive | Notes |
| --- | --- | --- |
| windows-x64 | `paraview-windows-x64.zip` | `bin/pvpython.exe`; Win32 offscreen OpenGL (no osmesa.dll in the official build — a software GL is a node concern) |
| linux-x64 | `paraview-linux-x64.zip` | `bin/pvpython` is Kitware's ELF launcher (sets `LD_LIBRARY_PATH`, falls back to the bundled `lib/mesa` OSMesa) exec'ing `pvpython-real`; the resolver restores both execute bits. Renders through llvmpipe OSMesa — `VTK_DEFAULT_OPENGL_WINDOW=vtkOSOpenGLRenderWindow`, set by `ParaViewRunnerEnvironment`; no GPU, display or system Mesa needed. **Nodes must provide** `libgomp1 libpciaccess0 libx11-6 libxext6` (Debian/Ubuntu names; X11 is dlopen'd even for offscreen rendering). Certified in a bare `ubuntu:22.04` container. |
| macos-arm64 | `paraview-macos-arm64.zip` | `ParaView-6.1.1.app/Contents/bin/pvpython`; best effort — packed from the official dmg but not run on macOS hardware yet |

`RuntimeTools/` is author-side: `generate_fixtures.py` (the golden corpus; `--plugin/--frd` add the
reader states), `generate_gui_states.py` (run INSIDE the ParaView GUI with `--script`: re-saves every
corpus scene from the GUI and adds GUI-native scenes — colour legend, text and time annotations, the
common filters, a chart view in a split layout — because a real client's state is GUI-saved and carries
proxies pvpython never writes), `generate_allowlist.py` (the proxy allowlist from the corpus + the live
registration, GUI states included; `--plugin` classifies the reader's proxies), `generate_frd_fixtures.py`
+ `check_frd_reader.py` (the reader proof on real CalculiX output), `verify_consumer.sh` (below),
`trim_paraview.py`, `collapse_symlinks.py`, `pe_closure.py` / `elf_closure.py` / `macho_closure.py`
(import-closure reports that guide the trim rules).

### Consumer verification (`RuntimeTools/verify_consumer.sh`)

The closest local stand-in for "published and deployed": builds the three packages (Release —
`GeneratePackageOnBuild`; a bare `dotnet pack` can serve a stale Release output), builds a throw-away
consumer that references them like WitCloud does (isolated NuGet packages folder; Grid + Variables from
nuget.org), which stages the module and fetches the three `paraview-v<ver>` zips from the GitHub Release
(SHA-verified, extracted to `paraview.module/paraview/<platform>/`), asserts the layout (every
platform's pvpython, the Linux launcher pair and Mesa, licenses, the embedded runner / allowlist / reader,
the scripts), and then renders corpus scenes through the consumer's modules with
`OutWit.Controller.Visualization.ParaView.Tests.ConsumerRunner` — a node-like process that references
only the engine SDK, so every controller type (and the `Assembly.Location` the runtime resolver reads)
comes from the consumer's `paraview.module`: stills, a GUI-saved scene, a reader scene, the PVD series
(5 frames). The in-process test suite cannot do that last step faithfully: it references the controller
project and its bin copy shadows the module's assembly. Runs on Windows (git-bash) and in a
`mcr.microsoft.com/dotnet/sdk:10.0` container with `libgomp1 libpciaccess0 libx11-6 libxext6`.
The author recipe per platform (official archive → `@Prerequisites/paraview/<platform>`):

```
# Windows (extract the official zip first)
python trim_paraview.py --platform windows-x64 --source <extracted> --out @Prerequisites/paraview/windows-x64
# Linux (inside ubuntu:22.04 — the tarball's symlinks need a POSIX filesystem)
python3 trim_paraview.py --platform linux-x64 --source <extracted> --out linux-x64
python3 collapse_symlinks.py linux-x64 --keep-alias "libospray_module_*" --keep-alias "libopenvkl_module_cpu_device.so"
# macOS (extract ParaView-6.1.1.app from the dmg with 7-Zip ≥ 22; HFS+ symlinks come out as real links)
python trim_paraview.py --platform macos-arm64 --source <ParaView-6.1.1.app> --out @Prerequisites/paraview/macos-arm64/ParaView-6.1.1.app
python collapse_symlinks.py @Prerequisites/paraview/macos-arm64/ParaView-6.1.1.app --keep-alias "libospray_module_*" --keep-alias "libopenvkl_module_cpu_device.dylib"
# then: outwit-assets-pack <csproj> --prerequisites @Prerequisites --apply --push-release
```

OSPRay and Open VKL dlopen their device modules by the *unversioned* name, which is why those aliases
survive the collapse; everything else is resolved by SONAME / install name.

## Limits (version 1)

Bytes are the primary dimension (`Validation/ParaViewInputLimits.cs`): 64 GiB per package, 16 GiB per task
subset, 32 MiB state, 10 000 attachments, 10 000 outputs, 16 384 px per dimension, 128 MP per frame,
1 GiB per output; XML depth 64, 2 M elements, 8 M attributes.

## Layout

Packages follow folders (namespace = `OutWit.Controller.Visualization.ParaView.<Folder>`):

```
Activities/    WitActivityParaView{Validate,Split,RenderFrame,Collect,CollectStill,Compose}
Adapters/      WitActivityAdapterParaView{...} — the executors
Variables/     WitVariableParaView{SceneRef,DataScene,OutputOptions,ValidationReport,RenderTask,RenderResult}
Collections/   WitVariableParaView{RenderTaskCollection,RenderResultCollection}
State/         ParaViewStateDocument (+Parser, Proxy, Property, CollectionItem, FormatException) — the hardened .pvsm reader
Validation/    ParaViewPackageValidator, ParaViewProxyAllowlist (+Document), ParaViewProxyPolicy, ParaViewLogicalPath,
               ParaViewCompatibility, ParaViewInputLimits, ParaViewFrameSelectionResolver, ParaViewDataSceneValidator
Tasks/         ParaViewTaskSplitter, ParaViewAttachmentSubsetIndex, ParaViewPackageDigest, ParaViewResultOrdering
Runtime/       ParaViewRuntimeInfo, ParaViewBinaryResolver, ParaViewRunnerEnvironment, ParaViewRunnerTask/Status (the documents),
               ParaViewTaskWorkspace, ParaViewTaskExecutor, ParaViewComposeTask/Status/Tokens, ParaViewComposeExecutor,
               ParaViewComposeBenchmark, ParaViewMaterializedAttachment
Processes/     ParaViewProcessRunner, ParaViewProcessOutcome, ParaViewProcessOutputTail, ProcessTreeGuard
Output/        ParaViewImageInfo, ParaViewImageFormats, ParaViewOutputValidator
Utils/         NullBlobService
Runner/        render_task.py, compose_scene.py (embedded)
Fixtures/      benchmark.frd (embedded; the compose benchmark's data — the corpus static.frd)
Plugins/       omnibuscloud_frd_reader.py (embedded; byte-identical with the GUI plugin's copy)
Allowlists/    paraview-6.1.json (embedded; generated from the fixture corpus by RuntimeTools/generate_allowlist.py)
RuntimeTools/  author-side: corpus + allowlist generators, frd fixtures + reader proof, runtime trimming, closure reports
Scripts/       the bundled .wit scripts
```

## Testing

`OutWit.Controller.Visualization.ParaView.Tests` runs without a ParaView runtime: wire-layout and round-trip
guards for the model, the security fixtures (XXE, entity bombs, deep/oversized XML, programmable
proxies, client paths, traversal, unknown plugins), validator/splitter/resolver/ordering logic, and the
bundled scripts end to end through the engine (host + worker node, blob transport, Grid dispatch) against
`OutWit.Controller.Visualization.ParaView.Tests.FakePvpython` — a stand-in that honors the runner contract
(schema 2: it loops over the outputs and can fail one of them, `FAKE-FAIL-OUTPUT-<n>`), parses the state
like the real runner and refuses a file reference the task did not materialize, which is how the suite
proves per-task subsetting — and, for the batch chain, the per-chunk download census (a 30-timestep job
in chunks of two: mesh and anchor once per chunk, every other piece once). `Runtime/ParaViewRunnerScriptTests`
runs the REAL `render_task.py` under a local Python over a stub `paraview` package, including a
three-output batch (one state load, camera restored between outputs) and an all-or-nothing failure.

`Activities/ParaViewRealRuntimeTests` (`[Category("RealRuntime")]`) runs the bundled scripts through the
engine against a **real** pvpython over the golden corpus (`Fixtures/Corpus`, generated by
`RuntimeTools/generate_fixtures.py` with the pinned ParaView): stills, every frame of the PVD series with
subset-only downloads (index + series anchor + own piece), the file-series reader, transparent PNGs,
the `.frd` states through the bundled reader (a warped static result, a quadratic element, a transient
heat transfer and a set of mode shapes whose frames must all differ), GUI-saved states (root
`<ParaView>`, legends, annotations, a chart view next to the render view), the reader's element-mapping
proof and a wall-clock kill of the whole runtime process tree — and asserts that frames of different
timesteps differ (the series contours a wavelet of growing amplitude, so a task silently rendering its
anchor piece would be caught). Two batch cases render the PVD series as ONE chunk through the real
runtime (five distinct frames, one materialization, and the same frames as five single tasks — 4.8×
faster on the dev box) and measure the batch benchmark (8 frames per cycle, a higher rate than the
single-frame cycle). On the production fleet (2026-08-26, three nodes, controller 0.5.0) the
60-frame scale job went from 88 s to 34 s wall (2.6×; 20 chunks of 3 balanced 9/6/5, nodes finishing
within 3 s of each other) and the nodes' batch rates are 5.8–6.4× their single-frame rates.
It auto-skips without a runtime; point `OUTWIT_PVPYTHON` at one or place
a runtime under `@Prerequisites/paraview/<platform>`. When no view is requested the validator renders
the first 3D render view, not the first registered view (the GUI lists chart and spreadsheet views ahead
of it).
The Linux runtime is certified by the same corpus through the runner in a bare `ubuntu:22.04` container
(see `@Prerequisites/paraview/linux-work` on the author machine).
