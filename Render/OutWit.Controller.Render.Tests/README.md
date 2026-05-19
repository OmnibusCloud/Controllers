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

An identical fixture set is staged in the upstream (closed-source) OmnibusCloud runtime repo, separately staged here so the Controllers repo can be cloned and tested independently. Drop your own copies of these files into `@Prerequisites/` / `@Data/` to unlock the corresponding tests; without them the integration tests `Assert.Ignore` gracefully.

## Without prerequisites

Tests that need an asset call `Assert.Ignore("<asset> not found at <path>")` instead of failing. To list what got skipped:

```powershell
dotnet test Render/OutWit.Controller.Render.Tests/OutWit.Controller.Render.Tests.csproj --logger "console;verbosity=normal" | Select-String 'Skipped'
```

## Golden-file workflow

Image-output tests call `RenderGoldenFileAssert.AssertImageMatches(...)` with the test key, render engine, and resolution. The helper:

1. **Always stages a candidate** at `@Output/GoldenCandidates/<testKey>_<Engine>_<WxH>.png` plus a side-by-side `..._diff.png` (actual | golden | amplified diff) so a failed comparison shows _what_ changed.
2. **Compares with tolerance** — mean absolute per-channel RGB difference, with a per-engine band (Cycles is stochastic so the tolerance is wider; Eevee / GreasePencil are tighter). SHA-256 is intentionally **not** used: Cycles 4-sample renders never match byte-for-byte across GPU/CPU/driver.
3. **Skips when golden is missing** (`Assert.Ignore`) with a clear pointer to the candidate path.

First-time approval:

```powershell
dotnet test ...                        # populates @Output/GoldenCandidates/
# Eyeball each candidate. If correct:
$env:WIT_RENDER_UPDATE_GOLDENS = '1'
dotnet test ...                        # auto-promotes candidates to @Prerequisites/render-golden/
Remove-Item Env:\WIT_RENDER_UPDATE_GOLDENS
dotnet test ...                        # verifies against the promoted goldens
```

Re-baselining an existing golden after an intentional renderer change: the same `WIT_RENDER_UPDATE_GOLDENS=1` flow — it overwrites existing goldens, not just missing ones.

`@Output/` is gitignored; `@Prerequisites/render-golden/` is gitignored as part of the prerequisites set (each developer / CI runner maintains its own approved goldens).
