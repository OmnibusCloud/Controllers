# OutWit.Controller.Assets.Pack

Author-side CLI for **Tier-2** controllers. Reads a controller `.csproj`,
finds every `<ControllerDataAsset>` declaration, packs the matching
prerequisite bytes (zip-folder or single-file), computes SHA256, rewrites
the csproj with the new SHA / Size / Uri, and (optionally) creates or
updates the matching GitHub Release with all artifacts uploaded.

Counterpart to `OutWit.Engine.Assets.MSBuild.ResolveControllerAssetsTask`,
which is the **consumer-side** task that fetches and extracts these assets
at build time on the consumer's machine.

## Install

```sh
dotnet tool install -g OutWit.Controller.Assets.Pack
```

The tool is published as `outwit-assets-pack` on the PATH.

## Quick start

For the `OutWit.Controller.Render` controller after replacing Blender:

```sh
# 1. Update @Prerequisites/blender/{windows-x64,linux-x64,macos-arm64}/
#    with the new Blender install trees.
# 2. Pack + update csproj + push release in one command:
outwit-assets-pack Render/OutWit.Controller.Render/OutWit.Controller.Render.csproj \
    --prerequisites C:/Workspace/OutWit/WitEngine/@Prerequisites \
    --version 1.15.2 \
    --apply \
    --push-release

# 3. Commit the csproj change, push, trigger the Publish workflow.
```

## Author-side csproj metadata

In addition to the standard `<ControllerDataAsset>` fields that the consumer
uses (`Uri`, `Sha256`, `Size`, `ExtractTo`, `RuntimeIdentifier`, `Required`),
each asset needs two author-only fields that this tool reads:

```xml
<ControllerDataAsset Include="blender-win-x64">
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <Uri>gh-release://OmnibusCloud/Controllers/render-v1.15.1/blender-windows-x64.zip</Uri>
  <Sha256>0000000000000000000000000000000000000000000000000000000000000000</Sha256>
  <Size>0</Size>
  <ExtractTo>blender/windows-x64/</ExtractTo>

  <!-- Author-only — never serialised into controller.json -->
  <PackSource>blender/windows-x64</PackSource>
  <PackKind>zip-folder</PackKind>
</ControllerDataAsset>
```

| Field | What it means |
| --- | --- |
| `PackSource` | Path relative to `--prerequisites`. For `zip-folder` it must be a directory; for `single-file` it must be a file. |
| `PackKind` | `zip-folder` recursively zips the directory (no leading folder entry). `single-file` copies the file verbatim. Default `single-file` if omitted. |

## Options

| Flag | Default | Description |
| --- | --- | --- |
| `<csproj>` | — | Positional path to the controller .csproj. **Required.** |
| `-p, --prerequisites <dir>` | — | Root containing the author-side binaries. **Required.** |
| `-o, --output <dir>` | `<repo>/dist/<controller>/` | Output directory for staged artifacts. |
| `--apply` | off | Rewrite the csproj in place. Without it the tool is dry-run. |
| `--clean` | off | Wipe the output directory before staging. |
| `-v, --version <X.Y.Z>` | csproj's current `<Version>` | Bump the package version to this value. Also rewrites the tag segment of every Uri. |
| `--release-tag <tag>` | `<controller>-v<version>` | Override the GitHub Release tag. |
| `--push-release` | off | After packing, create / update the GitHub Release and upload every staged artifact. |
| `--owner <org>` | `OmnibusCloud` | GitHub owner / org. |
| `--repo <name>` | `Controllers` | GitHub repository name. |
| `--token <pat>` | — | GitHub PAT passed inline. Overrides `--token-env`. |
| `--token-env <name>` | `GH_PACKAGE_TOKEN` | Env var name to read for the PAT. Token needs `contents:write` on the target repo. |
| `-h, --help` | — | Show help. |

## GitHub token

Recommended: set the token once in your environment under any descriptive name.

```sh
# PowerShell
$env:GH_PACKAGE_TOKEN = "github_pat_..."

# bash / zsh
export GH_PACKAGE_TOKEN="github_pat_..."
```

The default env var name is `GH_PACKAGE_TOKEN`. Override with
`--token-env GH_CUSTOM_NAME` if your environment uses a different convention.

The token needs `contents: write` permission on the target repo (no other
scope is required). Generate it as a fine-grained PAT at
https://github.com/settings/tokens?type=beta.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success (or `--help` / `--version` shown). |
| 1 | Runtime error (missing files, packing failure, REST API error, etc.). |
| 2 | Bad command-line arguments. |

## License

MIT.
