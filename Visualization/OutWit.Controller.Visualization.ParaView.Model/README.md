# OutWit.Controller.Visualization.ParaView.Model

Shared data types for the [OutWit.Controller.Visualization.ParaView](../OutWit.Controller.Visualization.ParaView/README.md)
controller — the OmnibusCloud headless ParaView renderer.

| Type | Script type | Purpose |
|---|---|---|
| `ParaViewSceneRefData` | `ParaViewSceneRef` | The visualization package: state blob (+ digest, size), content-addressed attachments with logical paths, roles, file-series group and per-timestep association, runtime requirements, the producer's timeline, and the opaque package manifest. |
| `ParaViewAttachmentRefData` | — | One package file: blob, normalized logical path, SHA-256, size, role, series group, timestep indices, ordinal. |
| `ParaViewRuntimeRequirementData` / `ParaViewPluginRequirementData` | — | Producing ParaView version (+ provenance) and the non-built-in plugins the state requires. |
| `ParaViewOutputOptionsData` | `ParaViewOutputOptions` | View, size, format, transparency, and the frame selection. |
| `ParaViewFrameSelectionData` | — | Single / Range / All / Explicit timestep selection. |
| `ParaViewRenderTaskData` | `ParaViewRenderTask` (+ `Collection`) | One unit of distributed work: state, view, timestep, options and **the task's minimal attachment subset**. Internal to the controller. |
| `ParaViewRenderResultData` | `ParaViewRenderResult` (+ `Collection`) | One rendered output: task identity, image blob, validated dimensions, backend versions, duration. |
| `ParaViewValidationReportData` | `ParaViewValidationReport` | Outcome of `ParaView.Validate`: errors, warnings, subsetting fallbacks, package digest, resolved view / timeline / indices / size. |

All types derive from `OutWit.Common.Abstract.ModelBase`, are `[MemoryPackable(GenerateType.VersionTolerant)]`
with an explicit `[MemoryPackOrder]` on every member (append-only evolution), and implement `Is`/`Clone`.

## Job document vocabulary (`paraview.*@1`)

The request and result types are published as `[JobDocumentContract]` types so non-.NET initiators —
first of all the OmnibusCloud ParaView plugin — submit jobs through the native SDK's job request
documents and read results back as value documents:

| Type id | Type |
|---|---|
| `paraview.sceneRef@1` | `ParaViewSceneRefData` |
| `paraview.attachmentRef@1` | `ParaViewAttachmentRefData` |
| `paraview.runtimeRequirement@1` | `ParaViewRuntimeRequirementData` |
| `paraview.pluginRequirement@1` | `ParaViewPluginRequirementData` |
| `paraview.outputOptions@1` | `ParaViewOutputOptionsData` |
| `paraview.frameSelection@1` | `ParaViewFrameSelectionData` |
| `paraview.turntable@1` | `ParaViewTurntableData` (Model 0.2.0: optional camera orbit inside the output options) |
| `paraview.renderResult@1` | `ParaViewRenderResultData` |
| `paraview.validationReport@1` | `ParaViewValidationReportData` |
| `paraview.dataScene@1` | `ParaViewDataSceneData` (Model 0.3.0: bare data + presentation choices, composed on the fleet by `ParaView.Compose` into a `paraview.sceneRef@1`) |

`Documents/` holds the generated artifacts (`paraview.schema.json`, `paraview_documents.hpp`,
`paraview_documents.py`), regenerated on every build by `OutWit.Cloud.Documents.Generator` and
verified in CI; they ship in this package under `documents/`. A C++ host (the ParaView plugin)
vendors `paraview_documents.hpp` and composes, for example,
`RenderParaViewFrames(ParaViewSceneRef, ParaViewOutputOptions)` from typed bindings; see the
[controller author guide](../../docs/controller-author-guide.md#non-net-initiators-publishing-a-document-vocabulary).

## Package invariants the controller enforces

- Logical paths are relative, `/`-separated, without `..`, drive letters, URI schemes or absolute roots.
- Every attachment declares size and SHA-256; the node verifies both while materializing.
- The state refers only to logical package paths; the runner maps them under the task's package root.
- The timeline comes from the state's TimeKeeper; `TimestepValues` on the scene reference is the producer's view of it.
- `TimestepIndices` on an attachment are the indices at which the file is displayed; an empty list means
  "every timestep". A series member with an empty list makes its whole group ship to every task
  (a recorded fallback).
