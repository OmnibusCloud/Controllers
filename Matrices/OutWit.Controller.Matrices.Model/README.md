# OutWit.Controller.Matrices.Model

Shared data types for the [`OutWit.Controller.Matrices`](https://www.nuget.org/packages/OutWit.Controller.Matrices) controller — matrix and vector classes (dense and sparse) used both host-side (in adapters) and node-side (in distributed batch processing).

Lives as a separate NuGet so:

- Cross-controller hosts and external tooling can reference the matrix/vector types directly without taking the whole controller as a runtime dep.
- The Matrices controller package can stay minimal and content-only (DLLs ship in its `content/module/`, not `lib/`).

This package is a transitive dependency when you `dotnet add package OutWit.Controller.Matrices` — you don't usually reference it directly unless you're building tooling on top of these types.

## License

MIT. See [LICENSE](https://github.com/OmnibusCloud/Controllers/blob/main/LICENSE).
