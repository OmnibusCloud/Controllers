# OutWit.Controller.Render

Distributed rendering controller for WitCloud. Current scope is a frame-based `.blend` rendering flow using Blender CLI across multiple compute nodes, with first-release engine-family support for `Cycles`, `Eevee` / `Eevee Next`, and `Grease Pencil`.

The upstream neutral DCC scene import/build boundary now lives in the separate host-only `OutWit.Controller.Render.Dcc` controller so this controller can remain focused on prepared `.blend` validation, preflight, rendering, collection, and encoding.

The canonical frozen public API reference for the first production version lives in `@Docs/Active/Render/RenderControllerApiFreeze.md`.

## Benchmark assets

Canonical benchmark assets for node-side render benchmarks live under `@Prerequisites/benchmark/render/` so the exact same `.blend` files can be versioned and packaged for every machine.

Maintainer regeneration scripts live under `Controllers/Render/OutWit.Controller.Render/benchmarks/`:

- `create_benchmark_still.py`
- `create_benchmark_video.py`

Those scripts are only for regenerating the canonical committed assets. Runtime and client benchmark execution must use the bundled canonical benchmark files, not locally generated ad-hoc files.

## Features

### Activities

| Activity | Side | Description |
|----------|------|-------------|
| `Render.Split` | Host | Generates `RenderTaskCollection` for a frame range. |
| `Render.SplitTiles` | Host | Generates tile-oriented `RenderTaskCollection` for a still frame. |
| `Render.Frame` | Node | Renders one `RenderTask`. Downloads `.blend` from blob storage, runs Blender CLI, uploads result. |
| `Render.Collect` | Host | Sorts `RenderResultCollection` and returns ordered frame blobs. |
| `Render.CollectStill` | Host | Validates a single still-frame result and returns one final image blob for the public still script surface. |
| `Render.CollectTiles` | Host | Validates complete rectangular tile coverage and tile dimensions, then stitches tile render results into one final still image using ffmpeg. |
| `Render.BuildBlend` | Host | Bootstrap typed-scene activity that uploads an inline prepared `.blend` payload and returns its blob id. |
| `Render.BuildBlendFromRefs` | Host | Bootstrap typed-scene activity that validates and returns an existing `.blend` blob reference. |
| `Render.EncodeVideo` | Host | Encodes an ordered frame blob sequence into an MP4 video using ffmpeg. |
| `Render.BlenderVersion` | Node | Returns the Blender version string available in the packaged runtime. |
| `Render.PreflightStillTiled` | Node | Validates whether the current packaged runtime can execute a tiled still request and returns blocking issues without starting a render. |
| `Render.RuntimeDiagnostics` | Node | Returns packaged Blender/ffmpeg/ffprobe availability, versions, and tiled-stitch capability diagnostics for the current runtime. |
| `Render.ValidateBlend` | Node | Validates a prepared `.blend` blob and returns serialized `RenderValidateBlendData` diagnostics JSON. |

### Variable Types

| Type | Description |
|------|-------------|
| `RenderOptions` | Render parameters: format (PNG/EXR/JPEG), engine family (`Cycles`, `Eevee` / `Eevee Next`, `Grease Pencil`), samples, resolution, denoise |
| `TileOptions` | Tile-specific parameters for tiled still workflows: `OverlapPx` plus stitch mode (`CenterPriorityCrop` or `AlphaBlend`) |
| `VideoOptions` | Video encoding parameters for the first production path: MP4/H.264 frame rate + CRF |
| `RenderScene` | Bootstrap typed-scene payload that currently carries an inline prepared `.blend` file |
| `RenderSceneRef` | Bootstrap blob-backed typed-scene reference that currently points at an existing `.blend` blob |
| `RenderTask` | Self-contained render task: scene blob ID, frame, tile region, task index, options |
| `RenderTaskCollection` | Collection of render tasks |
| `RenderResult` | Result of one rendered frame: index + blob ID of the rendered image |
| `RenderResultCollection` | Collection of render results |
| `RenderPreflightFrames` | Preflight result for still/frame-range requests: runtime target, renderability flag, and blocking issues |
| `RenderPreflightStillTiled` | Preflight result for tiled still requests: runtime target, requested blend mode, renderability flag, and blocking issues |
| `RenderPreflightVideo` | Preflight result for video requests: runtime target, renderability flag, and blocking issues |
| `RenderPreflight` | Unified preflight result containing runtime diagnostics plus still, frames, tiled-still, and video readiness summaries |
| `RenderRuntimeDiagnostics` | Packaged runtime diagnostics: runtime target, Blender/ffmpeg/ffprobe availability + versions, and tiled stitch mode support |

### Public Script API

Bundled scripts are the primary public API surface of the render controller. The final v1 contract is:

#### Output conventions

| Render mode | Final output contract |
|------------|-----------------------|
| Still | `Blob` |
| Frame sequence | `BlobCollection` |
| Tiled still | `Blob` |
| Video | `Blob` |

#### Blob-backed prepared-scene render scripts

| Script | Inputs | Output |
|--------|--------|--------|
| `RenderStill` | `RenderSceneRef:scene`, `Int:frame`, `RenderOptions:options` | `Blob:result` |
| `RenderStillCycles` | `RenderSceneRef:scene`, `Int:frame`, `RenderOptions:options` | `Blob:result` |
| `RenderStillEevee` | `RenderSceneRef:scene`, `Int:frame`, `RenderOptions:options` | `Blob:result` |
| `RenderStillGreasePencil` | `RenderSceneRef:scene`, `Int:frame`, `RenderOptions:options` | `Blob:result` |
| `RenderFrames` | `RenderSceneRef:scene`, `Int:startFrame`, `Int:endFrame`, `RenderOptions:options` | `BlobCollection:result` |
| `RenderFramesCycles` | `RenderSceneRef:scene`, `Int:startFrame`, `Int:endFrame`, `RenderOptions:options` | `BlobCollection:result` |
| `RenderFramesEevee` | `RenderSceneRef:scene`, `Int:startFrame`, `Int:endFrame`, `RenderOptions:options` | `BlobCollection:result` |
| `RenderFramesGreasePencil` | `RenderSceneRef:scene`, `Int:startFrame`, `Int:endFrame`, `RenderOptions:options` | `BlobCollection:result` |
| `RenderStillTiled` | `RenderSceneRef:scene`, `Int:frame`, `Int:tilesX`, `Int:tilesY`, `RenderOptions:options`, `TileOptions:tileOptions` | `Blob:result` |
| `RenderStillTiledCycles` | `RenderSceneRef:scene`, `Int:frame`, `Int:tilesX`, `Int:tilesY`, `RenderOptions:options`, `TileOptions:tileOptions` | `Blob:result` |
| `RenderStillTiledEevee` | `RenderSceneRef:scene`, `Int:frame`, `Int:tilesX`, `Int:tilesY`, `RenderOptions:options`, `TileOptions:tileOptions` | `Blob:result` |
| `RenderStillTiledGreasePencil` | `RenderSceneRef:scene`, `Int:frame`, `Int:tilesX`, `Int:tilesY`, `RenderOptions:options`, `TileOptions:tileOptions` | `Blob:result` |
| `RenderVideo` | `RenderSceneRef:scene`, `Int:startFrame`, `Int:endFrame`, `RenderOptions:options`, `VideoOptions:video` | `Blob:result` |
| `RenderVideoCycles` | `RenderSceneRef:scene`, `Int:startFrame`, `Int:endFrame`, `RenderOptions:options`, `VideoOptions:video` | `Blob:result` |
| `RenderVideoEevee` | `RenderSceneRef:scene`, `Int:startFrame`, `Int:endFrame`, `RenderOptions:options`, `VideoOptions:video` | `Blob:result` |
| `RenderVideoGreasePencil` | `RenderSceneRef:scene`, `Int:startFrame`, `Int:endFrame`, `RenderOptions:options`, `VideoOptions:video` | `Blob:result` |

#### Typed-scene render scripts

| Script | Inputs | Output |
|--------|--------|--------|
| `RenderSceneStill` | `RenderScene:scene`, `Int:frame`, `RenderOptions:options` | `Blob:result` |
| `RenderDccSceneFrames` | `DccScene:scene`, `Int:startFrame`, `Int:endFrame`, `RenderOptions:options` | `BlobCollection:result` |
| `RenderDccSceneStill` | `DccScene:scene`, `Int:frame`, `RenderOptions:options` | `Blob:result` |
| `RenderDccSceneStillTiled` | `DccScene:scene`, `Int:frame`, `Int:tilesX`, `Int:tilesY`, `RenderOptions:options`, `TileOptions:tileOptions` | `Blob:result` |
| `RenderDccSceneVideo` | `DccScene:scene`, `Int:startFrame`, `Int:endFrame`, `RenderOptions:options`, `VideoOptions:video` | `Blob:result` |
| `RenderSceneStillLarge` | `RenderSceneRef:scene`, `Int:frame`, `RenderOptions:options` | `Blob:result` |
| `RenderSceneFrames` | `RenderScene:scene`, `Int:startFrame`, `Int:endFrame`, `RenderOptions:options` | `BlobCollection:result` |
| `RenderSceneFramesLarge` | `RenderSceneRef:scene`, `Int:startFrame`, `Int:endFrame`, `RenderOptions:options` | `BlobCollection:result` |
| `RenderSceneStillTiled` | `RenderScene:scene`, `Int:frame`, `Int:tilesX`, `Int:tilesY`, `RenderOptions:options`, `TileOptions:tileOptions` | `Blob:result` |
| `RenderSceneStillTiledLarge` | `RenderSceneRef:scene`, `Int:frame`, `Int:tilesX`, `Int:tilesY`, `RenderOptions:options`, `TileOptions:tileOptions` | `Blob:result` |
| `RenderSceneVideo` | `RenderScene:scene`, `Int:startFrame`, `Int:endFrame`, `RenderOptions:options`, `VideoOptions:video` | `Blob:result` |
| `RenderSceneVideoLarge` | `RenderSceneRef:scene`, `Int:startFrame`, `Int:endFrame`, `RenderOptions:options`, `VideoOptions:video` | `Blob:result` |

#### Diagnostics and preflight scripts

| Script | Inputs | Output |
|--------|--------|--------|
| `RenderBlenderVersion` | none | `String:result` |
| `RenderValidateBlend` | `RenderSceneRef:scene` | `String:result` (`RenderValidateBlendData` JSON) |
| `RenderRuntimeDiagnostics` | none | `RenderRuntimeDiagnostics:result` |
| `RenderPreflightStill` | `Int:frame`, `RenderOptions:options` | `RenderPreflightFrames:result` |
| `RenderPreflightFrames` | `Int:startFrame`, `Int:endFrame`, `RenderOptions:options` | `RenderPreflightFrames:result` |
| `RenderPreflightStillTiled` | `Int:tilesX`, `Int:tilesY`, `RenderOptions:options`, `TileOptions:tileOptions` | `RenderPreflightStillTiled:result` |
| `RenderPreflightVideo` | `RenderOptions:options`, `VideoOptions:video` | `RenderPreflightVideo:result` |
| `RenderPreflight` | `Int:frame`, `Int:startFrame`, `Int:endFrame`, `Int:tilesX`, `Int:tilesY`, `RenderOptions:options`, `TileOptions:tileOptions`, `VideoOptions:video` | `RenderPreflight:result` |

### Scripts

**RenderFrames** — Distributes animation frames across nodes from a blob-backed prepared scene reference:
```
Job:RenderFrames(RenderSceneRef:scene, Int:startFrame, Int:endFrame, RenderOptions:options)
{
    Blob:blend = Render.BuildBlendFromRefs(scene);
    RenderTaskCollection:tasks = Render.Split(blend, startFrame, endFrame, options);
    RenderResultCollection:rendered = Grid.ForEach(task in tasks)
        => Render.Frame(task);
    BlobCollection:result = Render.Collect(rendered, options);
}
```

**RenderStill** — Renders one frame from a blob-backed prepared scene reference and returns one final still image blob:
```
Job:RenderStill(RenderSceneRef:scene, Int:frame, RenderOptions:options)
{
    Blob:blend = Render.BuildBlendFromRefs(scene);
    RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
    RenderResultCollection:rendered = Grid.ForEach(task in tasks)
        => Render.Frame(task);
    Blob:result = Render.CollectStill(rendered, options);
}
```

**RenderSceneStillTiled** — Bootstrap typed-scene tiled still flow with inline prepared `.blend` bytes:
```
Job:RenderSceneStillTiled(RenderScene:scene, Int:frame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
{
    Blob:blend = Render.BuildBlend(scene);
    RenderTaskCollection:tasks = Render.SplitTiles(blend, frame, tilesX, tilesY, options, tileOptions);
    RenderResultCollection:rendered = Grid.ForEach(task in tasks)
        => Render.Frame(task);
    Blob:result = Render.CollectTiles(rendered, options, tileOptions);
}
```

**RenderDccSceneStillTiled** — Bootstrap tiled still flow directly from typed neutral `DccSceneData`:
```
Job:RenderDccSceneStillTiled(DccScene:scene, Int:frame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
{
    Blob:blend = Render.BuildBlendFromDccScene(scene);
    Render.ClearScene(scene);
    RenderTaskCollection:tasks = Render.SplitTiles(blend, frame, tilesX, tilesY, options, tileOptions);
    RenderResultCollection:rendered = Grid.ForEach(task in tasks)
        => Render.Frame(task);
    Blob:result = Render.CollectTiles(rendered, options, tileOptions);
}
```

**RenderDccSceneFrames** — Bootstrap frame-range rendering directly from typed neutral `DccSceneData`:
```
Job:RenderDccSceneFrames(DccScene:scene, Int:startFrame, Int:endFrame, RenderOptions:options)
{
    Blob:blend = Render.BuildBlendFromDccScene(scene);
    Render.ClearScene(scene);
    RenderTaskCollection:tasks = Render.Split(blend, startFrame, endFrame, options);
    RenderResultCollection:rendered = Grid.ForEach(task in tasks)
        => Render.Frame(task);
    BlobCollection:result = Render.Collect(rendered, options);
}
```

**RenderDccSceneVideo** — Bootstrap video rendering directly from typed neutral `DccSceneData`:
```
Job:RenderDccSceneVideo(DccScene:scene, Int:startFrame, Int:endFrame, RenderOptions:options, VideoOptions:video)
{
    Blob:blend = Render.BuildBlendFromDccScene(scene);
    Render.ClearScene(scene);
    RenderTaskCollection:tasks = Render.Split(blend, startFrame, endFrame, options);
    RenderResultCollection:rendered = Grid.ForEach(task in tasks)
        => Render.Frame(task);
    BlobCollection:frames = Render.Collect(rendered, options);
    Blob:result = Render.EncodeVideo(frames, video);
}
```

**RenderRuntimeDiagnostics** — Returns packaged runtime/tool diagnostics for the current node:
```
Job:RenderRuntimeDiagnostics()
{
    RenderRuntimeDiagnostics:result = Render.RuntimeDiagnostics();
}
```

**RenderBlenderVersion** — Returns the packaged Blender version string:
```
Job:RenderBlenderVersion()
{
    String:result = Render.BlenderVersion();
}
```

**RenderValidateBlend** — Validates that a blob-backed prepared scene reference can be materialized and opened by the packaged Blender runtime:
```
Job:RenderValidateBlend(RenderSceneRef:scene)
{
    Blob:blend = Render.BuildBlendFromRefs(scene);
    String:result = Render.ValidateBlend(blend);
}
```

**RenderPreflightStillTiled** — Validates whether a tiled still request can run on the current packaged runtime and reports blocking issues:
```
Job:RenderPreflightStillTiled(Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
{
    RenderPreflightStillTiled:result = Render.PreflightStillTiled(tilesX, tilesY, options, tileOptions);
}
```

**RenderPreflightVideo** — Validates whether a video render request can run on the current packaged runtime and reports blocking issues:
```
Job:RenderPreflightVideo(RenderOptions:options, VideoOptions:video)
{
    RenderPreflightVideo:result = Render.PreflightVideo(options, video);
}
```

**RenderPreflightFrames** — Validates whether a frame-range render request can run on the current packaged runtime and reports blocking issues:
```
Job:RenderPreflightFrames(Int:startFrame, Int:endFrame, RenderOptions:options)
{
    RenderPreflightFrames:result = Render.PreflightFrames(startFrame, endFrame, options);
}
```

**RenderPreflightStill** — Convenience single-frame preflight built on top of `Render.PreflightFrames`:
```
Job:RenderPreflightStill(Int:frame, RenderOptions:options)
{
    RenderPreflightFrames:result = Render.PreflightFrames(frame, frame, options);
}
```

**RenderPreflight** — Unified preflight contract that evaluates still, frame-range, tiled-still, and video readiness in one call:
```
Job:RenderPreflight(Int:frame, Int:startFrame, Int:endFrame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions, VideoOptions:video)
{
    RenderPreflight:result = Render.Preflight(frame, startFrame, endFrame, tilesX, tilesY, options, tileOptions, video);
}
```

**RenderSceneStillTiledLarge** — Bootstrap typed-scene tiled still flow with blob-backed prepared `.blend` references:
```
Job:RenderSceneStillTiledLarge(RenderSceneRef:scene, Int:frame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
{
    Blob:blend = Render.BuildBlendFromRefs(scene);
    RenderTaskCollection:tasks = Render.SplitTiles(blend, frame, tilesX, tilesY, options, tileOptions);
    RenderResultCollection:rendered = Grid.ForEach(task in tasks)
        => Render.Frame(task);
    Blob:result = Render.CollectTiles(rendered, options, tileOptions);
}
```

**RenderStillTiled** — Splits one still frame from a blob-backed prepared scene reference into tiles and stitches the rendered tiles back into one image:
```
Job:RenderStillTiled(RenderSceneRef:scene, Int:frame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
{
    Blob:blend = Render.BuildBlendFromRefs(scene);
    RenderTaskCollection:tasks = Render.SplitTiles(blend, frame, tilesX, tilesY, options, tileOptions);
    RenderResultCollection:rendered = Grid.ForEach(task in tasks)
        => Render.Frame(task);
    Blob:result = Render.CollectTiles(rendered, options, tileOptions);
}
```

**RenderSceneStill** — Bootstrap typed-scene still flow with inline prepared `.blend` bytes:
```
Job:RenderSceneStill(RenderScene:scene, Int:frame, RenderOptions:options)
{
    Blob:blend = Render.BuildBlend(scene);
    RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
    RenderResultCollection:rendered = Grid.ForEach(task in tasks)
        => Render.Frame(task);
    Blob:result = Render.CollectStill(rendered, options);
}
```

**RenderSceneStillLarge** — Bootstrap typed-scene still flow with blob-backed prepared `.blend` references:
```
Job:RenderSceneStillLarge(RenderSceneRef:scene, Int:frame, RenderOptions:options)
{
    Blob:blend = Render.BuildBlendFromRefs(scene);
    RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
    RenderResultCollection:rendered = Grid.ForEach(task in tasks)
        => Render.Frame(task);
    Blob:result = Render.CollectStill(rendered, options);
}
```

**RenderSceneFrames** — Bootstrap typed-scene animation flow with inline prepared `.blend` bytes:
```
Job:RenderSceneFrames(RenderScene:scene, Int:startFrame, Int:endFrame, RenderOptions:options)
{
    Blob:blend = Render.BuildBlend(scene);
    RenderTaskCollection:tasks = Render.Split(blend, startFrame, endFrame, options);
    RenderResultCollection:rendered = Grid.ForEach(task in tasks)
        => Render.Frame(task);
    BlobCollection:result = Render.Collect(rendered, options);
}
```

**RenderSceneFramesLarge** — Bootstrap typed-scene animation flow with blob-backed prepared `.blend` references:
```
Job:RenderSceneFramesLarge(RenderSceneRef:scene, Int:startFrame, Int:endFrame, RenderOptions:options)
{
    Blob:blend = Render.BuildBlendFromRefs(scene);
    RenderTaskCollection:tasks = Render.Split(blend, startFrame, endFrame, options);
    RenderResultCollection:rendered = Grid.ForEach(task in tasks)
        => Render.Frame(task);
    BlobCollection:result = Render.Collect(rendered, options);
}
```

**RenderVideo** — Renders an animation from a blob-backed prepared scene reference and encodes the ordered frame sequence into MP4:
```
Job:RenderVideo(RenderSceneRef:scene, Int:startFrame, Int:endFrame, RenderOptions:options, VideoOptions:video)
{
    Blob:blend = Render.BuildBlendFromRefs(scene);
    RenderTaskCollection:tasks = Render.Split(blend, startFrame, endFrame, options);
    RenderResultCollection:rendered = Grid.ForEach(task in tasks)
        => Render.Frame(task);
    BlobCollection:frames = Render.Collect(rendered, options);
    Blob:result = Render.EncodeVideo(frames, video);
}
```

**RenderSceneVideo** — Bootstrap typed-scene video flow with inline prepared `.blend` bytes:
```
Job:RenderSceneVideo(RenderScene:scene, Int:startFrame, Int:endFrame, RenderOptions:options, VideoOptions:video)
{
    Blob:blend = Render.BuildBlend(scene);
    RenderTaskCollection:tasks = Render.Split(blend, startFrame, endFrame, options);
    RenderResultCollection:rendered = Grid.ForEach(task in tasks)
        => Render.Frame(task);
    BlobCollection:frames = Render.Collect(rendered, options);
    Blob:result = Render.EncodeVideo(frames, video);
}
```

**RenderSceneVideoLarge** — Bootstrap typed-scene video flow with blob-backed prepared `.blend` references:
```
Job:RenderSceneVideoLarge(RenderSceneRef:scene, Int:startFrame, Int:endFrame, RenderOptions:options, VideoOptions:video)
{
    Blob:blend = Render.BuildBlendFromRefs(scene);
    RenderTaskCollection:tasks = Render.Split(blend, startFrame, endFrame, options);
    RenderResultCollection:rendered = Grid.ForEach(task in tasks)
        => Render.Frame(task);
    BlobCollection:frames = Render.Collect(rendered, options);
    Blob:result = Render.EncodeVideo(frames, video);
}
```

## Dependencies

- `Variables` — Blob type, Int, IntCollection
- `Grid` — Grid.ForEach for distributed execution

## Blender Integration

The controller expects portable Blender runtimes inside the `blender/` subdirectory of the controller module and portable ffmpeg runtimes inside the `ffmpeg/` subdirectory. The package currently carries platform-specific subdirectories and resolves the active runtime automatically.

The packaged controller manifest now records explicit archive compatibility conditions for each supported runtime target:
- `win-x64` + `windows` + `x64`
- `linux-x64` + `linux` + `x64`
- `osx-arm64` + `macos` + `arm64`

For the current tiled-still slice, tile collection is fail-fast:
- tile bounds must form a complete rectangular grid over the full `[0..1] x [0..1]` frame area
- tile image dimensions must match the expected rendered tile size after overlap expansion
- overlap stitching supports two modes:
  - `CenterPriorityCrop` — overlap regions are rendered, then cropped back to each tile's logical core area before overlay
  - `AlphaBlend` — overlap regions are feather-blended with normalized weight accumulation in managed RGBA composition before encoding the final image

### Supported Platforms

- Windows x64 (`blender.exe`)
- Linux x64 (`blender`)
- macOS ARM64 (`Blender.app`)

### Cross-Platform Path Resolution

The `BlenderRunner` utility uses `RenderBinaryResolver` to resolve the active runtime from the packaged `blender/<rid>/...` layout.
