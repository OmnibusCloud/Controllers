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
| `ParaView.Compose(ParaViewDataScene data, ParaViewOutputOptions options)` → `ParaViewSceneRef` | node | **0.3.0.** Composes a scene from BARE data — one blob-referenced CalculiX `.frd` plus the presentation choices of a data scene (colour array, colour-map preset, representation, scalar bar, camera direction, fit) — into a REAL saved state: materializes the data, runs the controller-owned composer (`compose_scene.py`: bundled reader → representation → colouring with one baked colour range → camera fitted to the union of the data bounds → `SaveState`, absolute data path rewritten to the logical path), hashes and publishes the state, stamps the data digest, and returns an ordinary package reference — after running it through the host validator itself, so a state the allowlist would refuse never leaves the node. One task per job through `Grid.Delegate()`. |
| `ParaView.Collect(rendered, options)` → `BlobCollection` | host | Restores task order, fails on missing/duplicate/conflicting identities. |
| `ParaView.CollectStill(rendered, options)` → `Blob` | host | Exactly one result → one image blob. |

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

`ParaView.RenderFrame` is the only distributed activity, so it is the only one with a measured node
benchmark (`Runtime/ParaViewBenchmark` + the embedded `Runner/benchmark_frames.py`). At startup every
worker runs the engine's benchmark pass: one pvpython process builds a procedural Wavelet scene
(61³ points contoured at four values, clipped and sliced) and renders 512×512 PNG frames while
rotating the camera. **Every frame re-executes the contour+clip pipeline** (one isosurface value
alternates between two fixed levels) — the cost a real task pays in every process; without it VTK's
filter caching leaves only rasterization + readback in the loop and a 32-core node measures nearly
the same as a 2-core one. `SaveScreenshot` is included, so readback and encoding count. The timed loop
runs `MinDuration` seconds (default 1.5 s from the engine, 3 s fallback, at most 120 frames, 1 warm-up
frame); the whole process is ~5–6 s and is killed at 5 minutes. The result is `paraview-pixels@v1`:
**output pixels per second** on dataset `paraview-benchmark-wavelet@v2`, with
`render-window`/`render-device` (`vtkOSOpenGLRenderWindow` = software), `render-frames`,
`render-seconds`, `paraview-version` and `scene-points` in `Custom`. A node without a usable runtime
reports rate 0.

Determinism: the scene, the camera step and the two alternating isosurface sets are fixed, so every
node times the same frames (per-frame spread across a full rotation measured within ±10%; the two
isosurface sets differ by under 1% of workload). The work estimate of a task is expressed in the same
unit — `pixels + materializedBytes / 64` — so the Grid allocator (`WitGridTaskAllocator`:
longest-processing-time first, rate-weighted, fewer nodes when the makespan does not suffer) hands a
GPU workstation proportionally more frames than a software-GL VM. Measured (v2): 5.1 M px/s on a
Windows GPU workstation, 3.3 M px/s under OSMesa with 32 cores, 1.3 M px/s under OSMesa throttled to
2 cores — a 3.9 : 2.5 : 1 spread where the cached-pipeline v1 loop saw only 2.6 : 1.3 : 1.

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
a colour array the data does not carry fails the job naming the arrays that exist. The composer
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

## The runner contract

The node never builds a shell string. It invokes the bundled `pvpython` directly:

```
pvpython --force-offscreen-rendering --disable-registry <work>/runner/render_task.py --task-file <work>/task.json
```

with cwd = the task's package root and an environment built from an allowlist
(`PATH` = the runtime's bin directory + system dirs, task-private `HOME`/`APPDATA`/`TEMP`,
`PYTHONNOUSERSITE=1`, no `DISPLAY`, no plugin paths; on Linux `VTK_DEFAULT_OPENGL_WINDOW=vtkOSOpenGLRenderWindow`
selects the software-rendering baseline). `task.json` carries the state path, the package root, the
output/status paths, view, timestep, size, format, the optional bundled-reader path, the effective proxy
allowlist and the blocked proxy/property lists (`Runtime/ParaViewRunnerTask.cs`); the runner writes
`status.json` on every exit path (`Runtime/ParaViewRunnerStatus.cs`) and exits non-zero on any discrepancy.
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
`OutWit.Controller.Visualization.ParaView.Tests.FakePvpython` — a stand-in that honors the runner contract,
parses the state like the real runner and refuses a file reference the task did not materialize, which is
how the suite proves per-task subsetting.

`Activities/ParaViewRealRuntimeTests` (`[Category("RealRuntime")]`) runs the bundled scripts through the
engine against a **real** pvpython over the golden corpus (`Fixtures/Corpus`, generated by
`RuntimeTools/generate_fixtures.py` with the pinned ParaView): stills, every frame of the PVD series with
subset-only downloads (index + series anchor + own piece), the file-series reader, transparent PNGs,
the `.frd` states through the bundled reader (a warped static result, a quadratic element, a transient
heat transfer and a set of mode shapes whose frames must all differ), GUI-saved states (root
`<ParaView>`, legends, annotations, a chart view next to the render view), the reader's element-mapping
proof and a wall-clock kill of the whole runtime process tree — and asserts that frames of different
timesteps differ (the series contours a wavelet of growing amplitude, so a task silently rendering its
anchor piece would be caught). It auto-skips without a runtime; point `OUTWIT_PVPYTHON` at one or place
a runtime under `@Prerequisites/paraview/<platform>`. When no view is requested the validator renders
the first 3D render view, not the first registered view (the GUI lists chart and spreadsheet views ahead
of it).
The Linux runtime is certified by the same corpus through the runner in a bare `ubuntu:22.04` container
(see `@Prerequisites/paraview/linux-work` on the author machine).
