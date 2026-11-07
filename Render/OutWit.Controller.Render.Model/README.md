# OutWit.Controller.Render.Model

Shared data types for the [`OutWit.Controller.Render`](https://www.nuget.org/packages/OutWit.Controller.Render) controller — render task, scene, options, and result data used both host-side (in adapters) and node-side (in distributed batch processing).

Lives as a separate NuGet so:

- Cross-controller hosts and external tooling can reference the render-data types directly without taking the whole controller as a runtime dep.
- The Render controller package can stay minimal and content-only (DLLs ship in its `content/module/`, not `lib/`).

This package is a transitive dependency when you `dotnet add package OutWit.Controller.Render` — you don't usually reference it directly unless you're building tooling on top of these types.

## Job document vocabulary (native initiators)

The request DTOs (`RenderSceneRefData`, `RenderSceneAttachmentRefData`,
`RenderOptionsData`, `TileOptionsData`, `VideoOptionsData`,
`RenderBakeOptionsData`) and the validate/preflight result DTOs are published
into the OmnibusCloud job document vocabulary as `[JobDocumentContract]`
types (`render.sceneRef@1`, `render.options@1`, `render.tileOptions@1`, …).
The `documents/` folder of the package — regenerated on every build by
`OutWit.Cloud.Documents.Generator` from these annotations and committed under
`Documents/` in the repository — carries what a non-.NET host needs to submit
render jobs through the OmnibusCloud native SDK without touching C#:

- `render.schema.json` — the JSON Schema fragment of the vocabulary;
- `render_documents.hpp` — header-only C++17 binding (`to_json()`, `to_parameter_json()`);
- `render_documents.py` — standard-library-only Python binding (`to_json()`, `from_json()`, `to_parameter()`).

The scripts consume the same positional signatures as ever, e.g.
`RenderStill(RenderSceneRef:scene, Int:frame, RenderOptions:options)`; a host
places `RenderSceneRef(...).to_parameter()` and `RenderOptions(...).to_parameter()`
into the job request document at those positions. Managed (.NET) hosts keep
using the typed SDK and never see the documents. Type ids are frozen once
published; a wire-shape change is a new major (`@2`).

## License

MIT. See [LICENSE](https://github.com/OmnibusCloud/Controllers/blob/main/LICENSE).
