# OutWit.Controller.Visualization.ParaView

Headless scientific visualization for OmnibusCloud: safely executes **OmnibusCloud ParaView packages**
(a saved `.pvsm` state plus content-addressed data attachments) and distributes independent rendering
tasks across worker nodes. The companion
[`OutWit.Controller.Visualization.ParaView.Model`](../OutWit.Controller.Visualization.ParaView.Model/README.md)
carries the shared data types and the `paraview.*@1` job document vocabulary for non-.NET initiators
(the ParaView GUI plugin).

**Status: in development.** The controller shape, validation, splitting, the node runner contract and
the test harness are complete; the per-platform ParaView runtime assets (runtime-proof milestone) and
the bundled OmnibusCloud `.frd` reader (reader milestone) follow.

## Activities

| Activity | Side | Purpose |
|---|---|---|
| `ParaView.Validate(ParaViewSceneRef scene, ParaViewOutputOptions options)` → `ParaViewValidationReport` | host | Treats the package as untrusted input: package reference, attachments and logical paths, runtime requirement (exact ParaView major.minor, allowlisted plugins only), output options and limits, then the state — hardened XML parse (no DTD/entities, depth/count/size bounds), proxy allowlist, programmable-pipeline rejection, file references against the package, views, timeline, frame selection. Downloads only the state. An invalid package is a *completed* activity with an invalid report. |
| `ParaView.Split(scene, report, options)` → `ParaViewRenderTaskCollection` | host | Deterministic tasks — one per resolved timestep of the resolved view; identity = package digest + dataset identity (reserved, empty) + view + timestep + options digest; **each task carries only the attachments its timestep needs** (series pieces by `TimestepIndices`, plus statics and series indexes). Refuses an invalid report. |
| `ParaView.RenderFrame(ParaViewRenderTask task)` → `ParaViewRenderResult` | node | Materializes the state and the task's subset into an isolated, task-unique package root (digests verified while copying, nothing outside the subset requested), writes the controller-owned runner, runs `pvpython` under an allowlisted environment, interprets exit code + status document, validates the output (signature, dimensions, alpha, no stray files), publishes it. Cancellation and the wall-clock limit kill the whole process tree; the workspace is deleted on every path. |
| `ParaView.Collect(rendered, options)` → `BlobCollection` | host | Restores task order, fails on missing/duplicate/conflicting identities. |
| `ParaView.CollectStill(rendered, options)` → `Blob` | host | Exactly one result → one image blob. |

## Bundled scripts (`OutWit.Controller.Visualization.ParaView.Scripts`)

```
RenderParaViewFrames(ParaViewSceneRef:scene, ParaViewOutputOptions:options)            -> BlobCollection:result
RenderParaViewStill (ParaViewSceneRef:scene, ParaViewOutputOptions:options)            -> Blob:result
RenderParaViewVideo (ParaViewSceneRef:scene, ParaViewOutputOptions:options, VideoOptions:video) -> Blob:result   (Render.EncodeVideo; requires the Render controller)
ValidateParaViewScene(ParaViewSceneRef:scene, ParaViewOutputOptions:options)           -> ParaViewValidationReport:result
```

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
Whether file-series readers need an anchor file in every subset is settled against the real runtime in the
distributed-animation milestone.
The runner (`Runner/render_task.py`), the proxy allowlist (`Allowlists/paraview-<major.minor>.json`) and
the bundled reader are **embedded in the controller assembly** and written per task into the task's own
work directory — the only plugin path the runner consults — so a node can never run a stale or foreign copy.

`OUTWIT_PVPYTHON` overrides the runtime location (operator escape hatch and the test seam).

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

`RuntimeTools/` is author-side: `generate_fixtures.py` (the golden corpus), `generate_allowlist.py`
(the proxy allowlist from the corpus + the live registration), `trim_paraview.py`, `collapse_symlinks.py`,
`pe_closure.py` / `elf_closure.py` (import-closure reports that guide the trim rules).

## Limits (version 1)

Bytes are the primary dimension (`Validation/ParaViewInputLimits.cs`): 64 GiB per package, 16 GiB per task
subset, 32 MiB state, 10 000 attachments, 10 000 outputs, 16 384 px per dimension, 128 MP per frame,
1 GiB per output; XML depth 64, 2 M elements, 8 M attributes.

## Layout

Packages follow folders (namespace = `OutWit.Controller.Visualization.ParaView.<Folder>`):

```
Activities/    WitActivityParaView{Validate,Split,RenderFrame,Collect,CollectStill}
Adapters/      WitActivityAdapterParaView{...} — the executors
Variables/     WitVariableParaView{SceneRef,OutputOptions,ValidationReport,RenderTask,RenderResult}
Collections/   WitVariableParaView{RenderTaskCollection,RenderResultCollection}
State/         ParaViewStateDocument (+Parser, Proxy, Property, CollectionItem, FormatException) — the hardened .pvsm reader
Validation/    ParaViewPackageValidator, ParaViewProxyAllowlist (+Document), ParaViewProxyPolicy, ParaViewLogicalPath,
               ParaViewCompatibility, ParaViewInputLimits, ParaViewFrameSelectionResolver
Tasks/         ParaViewTaskSplitter, ParaViewAttachmentSubsetIndex, ParaViewPackageDigest, ParaViewResultOrdering
Runtime/       ParaViewRuntimeInfo, ParaViewBinaryResolver, ParaViewRunnerEnvironment, ParaViewRunnerTask/Status (the documents),
               ParaViewTaskWorkspace, ParaViewTaskExecutor
Processes/     ParaViewProcessRunner, ParaViewProcessOutcome, ParaViewProcessOutputTail, ProcessTreeGuard
Output/        ParaViewImageInfo, ParaViewImageFormats, ParaViewOutputValidator
Utils/         NullBlobService
Runner/        render_task.py (embedded)
Allowlists/    paraview-6.1.json (embedded; generated from the fixture corpus by RuntimeTools/generate_allowlist.py)
RuntimeTools/  author-side: corpus + allowlist generators, runtime trimming, closure reports
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
subset-only downloads (index + series anchor + own piece), the file-series reader, transparent PNGs —
and asserts that frames of different timesteps differ (the series contours a wavelet of growing
amplitude, so a task silently rendering its anchor piece would be caught). It auto-skips without a
runtime; point `OUTWIT_PVPYTHON` at one or place a runtime under `@Prerequisites/paraview/<platform>`.
The Linux runtime is certified by the same corpus through the runner in a bare `ubuntu:22.04` container
(see `@Prerequisites/paraview/linux-work` on the author machine).
