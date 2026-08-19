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
stays opportunistic. The per-platform runtimes (win-x64 / linux-x64 / osx-arm64, trimmed of the Qt client,
with the ParaView license and SPDX materials) are declared as `<ControllerDataAsset>` entries on a
`paraview-v<ver>` GitHub Release and extracted to `paraview/<platform>/` — the runtime-proof milestone.

## Limits (version 1)

Bytes are the primary dimension (`Validation/ParaViewInputLimits.cs`): 64 GiB per package, 16 GiB per task
subset, 32 MiB state, 10 000 attachments, 10 000 outputs, 16 384 px per dimension, 128 MP per frame,
1 GiB per output; XML depth 64, 2 M elements, 8 M attributes.

## Layout

```
Activities/    WitActivityParaView{Validate,Split,RenderFrame,Collect,CollectStill}
Adapters/      the executors + ParaViewResultOrdering
Variables/     ParaViewSceneRef, ParaViewOutputOptions, ParaViewValidationReport, ParaViewRenderTask(+Collection), ParaViewRenderResult(+Collection)
Validation/    ParaViewStateDocument (hardened parser), ParaViewPackageValidator, ParaViewProxyAllowlist/Policy,
               ParaViewLogicalPath, ParaViewPackageDigest, ParaViewTaskSplitter, ParaViewFrameSelectionResolver, ParaViewCompatibility, ParaViewInputLimits
Runtime/       ParaViewBinaryResolver, ParaViewRunnerEnvironment, ParaViewProcessRunner, ParaViewTaskWorkspace, ParaViewTaskExecutor,
               ParaViewRunnerTask/Status (the documents), ParaViewImageInfo, ParaViewOutputValidator, ParaViewRuntimeInfo
Runner/        render_task.py (embedded)
Allowlists/    paraview-6.1.json (embedded; seed — regenerated from the fixture corpus by the runtime-proof tooling)
Scripts/       the bundled .wit scripts
```

## Testing

`OutWit.Controller.Visualization.ParaView.Tests` runs without a ParaView runtime: wire-layout and round-trip
guards for the model, the security fixtures (XXE, entity bombs, deep/oversized XML, programmable
proxies, client paths, traversal, unknown plugins), validator/splitter/resolver/ordering logic, and the
bundled scripts end to end through the engine (host + worker node, blob transport, Grid dispatch) against
`OutWit.Controller.Visualization.ParaView.Tests.FakePvpython` — a stand-in that honors the runner contract,
parses the state like the real runner and refuses a file reference the task did not materialize, which is
how the suite proves per-task subsetting. Real-runtime tests arrive with the runtime-proof milestone.
