# OutWit.Controller.Pack (`outwit-controller-pack`)

`.NET` global tool for third-party controller authors. Packages a built controller `.module/` directory + its `controller.json` manifest into a self-contained zip ready for upload to a WitCloud server via the admin UI.

## Install

```bash
dotnet tool install -g OutWit.Controller.Pack
```

## Usage

```bash
outwit-controller-pack <module-dir>
outwit-controller-pack --module <module-dir> [--output <zip-path>]
                       [--allow-external-uris]
```

By default, the tool requires every `dataAssets[]` entry in the manifest to use a `file://` URI that resolves to an existing file inside the module directory — contributor zips bundle assets inline so the admin reviewer sees the exact bytes that will reach clients. Use `--allow-external-uris` to opt in to `https://` / `gh-release://` URIs (the receiving server must also be configured to permit them).

See the [`controller-assets-architecture.md`](https://github.com/dmitrat/WitEngine/blob/main/@Docs/Active/Architecture/controller-assets-architecture.md) design doc (in the WitEngine repo) for the Path A vs Path B distribution split and the full contributor workflow.

## Source

The tool source lives here in [`OmnibusCloud/Controllers`](https://github.com/OmnibusCloud/Controllers) alongside the first-party controller examples — third-party authors can read it, fork it, contribute fixes. It depends on the published [`OutWit.Engine.Assets`](https://www.nuget.org/packages/OutWit.Engine.Assets) library for manifest schema parsing.
