# OutWit.Controller.Render.Tests

Test suite for the Render controller. Mostly fast unit tests that run anywhere; a smaller subset needs heavy external assets and is **explicitly opt-in via `Assert.Ignore`** when those assets are absent — CI stays green, local devs with everything in place run the full suite.

## Layout

```
Activities/         — controller activity tests
Mock/               — IWitBlobService test double
Utils/              — asset path resolver + golden-file helpers
Variables/          — variable serialization round-trip tests
Benchmark/          — RenderBenchmark adapter integration tests
```

## Local prerequisites (gitignored)

Tests that need real Blender scenes / golden frames / native runtime binaries look for them under `@Prerequisites/` and `@Data/` next to the solution root (`OutWit.slnx`). Both folders are gitignored — populate them locally to unlock the corresponding tests:

| Path | Used by | Size |
|---|---|---|
| `@Prerequisites/test_scene.blend` | `RenderBuildBlendTests` (2 attachment-materialization tests) | ~850 KB |
| `@Prerequisites/benchmark/render/*.blend` | `RenderBenchmarkIntegrationTests` | ~380 KB |
| `@Prerequisites/render-golden/` | golden-file frame assertions | varies |
| `@Prerequisites/blender/<os-arch>/blender(.exe)` | `BlenderRunnerIntegrationTests`, all `Activities/RenderFrame*` / `RenderTiled*` / `RenderProductionScript*`, also indirectly by `RenderBuildBlendFromRefs` attachment-rewriting tests | ~3 GB |
| `@Prerequisites/ffmpeg/` | `FfmpegRunnerTests`, `RenderEncodeVideoTests` | ~1.4 GB |
| `@Data/cube_diorama/` | `BlenderRunnerCubeDioramaDiagnosticsTests` | ~22 MB |

All of these live alongside an identical layout in the sibling `WitEngine` repo (`WitEngine/@Prerequisites/`, `WitEngine/@Data/`) — they are the same fixture set, separately staged here so the Controllers repo can be cloned and tested independently of WitEngine.

Quick bootstrap (when you also have the WitEngine repo checked out at `../OutWit/WitEngine`):

```powershell
# from Controllers repo root
$src = '..\..\OutWit\WitEngine'
robocopy "$src\@Prerequisites" .\@Prerequisites /E /XO
robocopy "$src\@Data\cube_diorama" .\@Data\cube_diorama /E /XO
```

## Without prerequisites

Tests that need an asset call `Assert.Ignore("<asset> not found at <path>")` instead of failing. To list what got skipped:

```powershell
dotnet test Render/OutWit.Controller.Render.Tests/OutWit.Controller.Render.Tests.csproj --logger "console;verbosity=normal" | Select-String 'Skipped'
```
