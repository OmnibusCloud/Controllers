# OutWit.Controller.Render.Dcc.Model

Shared data types for the [`OutWit.Controller.Render.Dcc`](https://www.nuget.org/packages/OutWit.Controller.Render.Dcc) controller — DCC scene-ref, attachment, and validation types used by the upstream DCC bootstrap activities and consumed by the downstream Render controller at runtime.

Lives as a separate NuGet so:

- Cross-controller hosts and external tooling can reference the DCC scene types directly without taking the whole controller as a runtime dep.
- The Render.Dcc controller package can stay minimal and content-only (DLLs ship in its `content/module/`, not `lib/`).

This package is a transitive dependency when you `dotnet add package OutWit.Controller.Render.Dcc` — you don't usually reference it directly unless you're building tooling on top of these types.

## License

MIT. See [LICENSE](https://github.com/OmnibusCloud/Controllers/blob/main/LICENSE).
