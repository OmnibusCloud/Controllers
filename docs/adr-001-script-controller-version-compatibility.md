# ADR 001 — Script ↔ Controller Version Compatibility

**Status**: Accepted (2026-05-22)

**Scope**: The three Scripts NuGet packages introduced in commit
[`6ef0c07`](https://github.com/OmnibusCloud/Controllers/commit/6ef0c07):

- `OutWit.Controller.Matrices.Scripts`
- `OutWit.Controller.Render.Scripts`
- `OutWit.Controller.Render.Dcc.Scripts`

and their relationship to the corresponding controller packages
(`OutWit.Controller.Matrices`, `OutWit.Controller.Render`,
`OutWit.Controller.Render.Dcc`).

## Context

The `.wit` scripts shipped in each Scripts package call activities
exposed by the matching controller (`Matrix.RowCount`,
`Grid.ForEach`, `Render.RenderFrame`, etc). Resolution happens at
**runtime by name**, not at compile time: the script text is parsed
by the WitEngine compiler in `ScriptSeeder.SeedFromFileAsync`
(WitCloud), which looks up each activity in the loaded
`WitControllerCollection`. There is no managed reference between the
Scripts assembly (which is content-only — no DLL) and the controller
DLL.

This means the **compatibility surface is the activity names + their
parameter signatures** at the script level, not the controller DLL's
binary API.

Consequences if compatibility breaks:

- Controller renames an activity → scripts referencing it fail
  compilation in `ScriptSeeder`. Approved scripts land with
  `Status = PendingValidation` + `CompilationError = "...activity
  not found..."`. Visible in the WitCloud admin UI but the cloud
  still starts.
- Controller changes an activity's parameter list → similar:
  compilation throws "type mismatch" or "wrong arity".
- Controller adds an activity → forward-compatible, scripts still work.
- Scripts package adds a new script using an activity the consumer's
  installed controller version doesn't have yet → again, fails at
  seed time with a clear compilation error.

The three Scripts packages today (all `1.0.0`) and their target
controllers (Matrices `1.0.5`, Render `1.15.3`, Render.Dcc `1.0.4`)
**version independently**. WitCloud references them via floating
constraints like `Version="1.0.*"` (Scripts) and `Version="1.15.*"`
(Render), so a fresh restore can pick up any patch- or minor-level
release of either side without csproj edits.

## Decision

1. **Versioning is independent and SemVer-based.** Each Scripts
   package gets its own version line that follows SemVer relative to
   its own content (the `.wit` files it ships):
   - Patch (`1.0.0 → 1.0.1`): bug fix inside a script body, no
     interface change to its consumers.
   - Minor (`1.0.0 → 1.1.0`): a new script added, no existing script
     changed in a way that breaks consumers seeding under the old
     version. (For ScriptSeeder, "consumers" means a deployed
     WitCloud installation that already has the old version's scripts
     persisted in `ScriptStore`.)
   - Major (`1.0.0 → 2.0.0`): the package drops or renames a script,
     OR requires a controller major bump to compile.

2. **Compatibility with the controller is communicated via a NuGet
   PackageReference dependency with a bracket version constraint.**
   Each Scripts package's csproj adds, for example:

   ```xml
   <PackageReference Include="OutWit.Controller.Render"
                     Version="[1.15.0, 2.0.0)" />
   ```

   - Lower bound = the minimum controller version whose activity
     surface the scripts in this package use.
   - Upper bound = the next controller major (exclusive). A
     controller major bump signals possibly-breaking activity
     renames, so a paired Scripts major bump is expected before that
     upper bound moves.

3. **No runtime check is added** beyond what already exists.
   `ScriptSeeder.SeedFromFileAsync` calls `Engine.Compile(scriptText)`
   which surfaces a `CompilationError` on each affected script. That
   error is persisted on `ScriptPackage.CompilationError` and
   rendered in the admin UI. Adding a startup-blocking check would
   prevent the rest of the cloud from coming up because of a
   per-script compile error, which is worse than the current
   degraded-but-running behavior.

4. **Scripts packages do not become a "meta-installer" for the
   controller.** The PackageReference declared in (2) is the
   compatibility marker NuGet uses at restore time; it also pulls
   the controller in transitively for any consumer that wants
   scripts but somehow doesn't yet reference the controller. That's
   a useful side effect — every realistic Scripts-pack consumer
   needs the matching controller — but the Scripts package is not
   responsible for the controller's content/module/ staging or asset
   resolution. Those still come from the controller package's own
   `build/.targets`.

## Consequences

### Positive

- A WitCloud csproj that uses `Version="1.0.*"` for the Scripts
  package can still receive Scripts patch releases without manual
  bumps.
- The bracket constraint makes a "Render 2.0.0 + Render.Scripts
  1.0.0" combination impossible to restore — NuGet rejects it with a
  clear "version conflict" message at the right moment (build), not
  at server startup minutes later.
- Compatibility intent is encoded **in the package itself**, not in
  an out-of-band doc that consumers may not read.
- No service-side code change required; the policy is enforced by
  NuGet and validated end-to-end by the
  [`verify-scripts-consumer.yml`](../.github/workflows/verify-scripts-consumer.yml)
  workflow.

### Negative

- A Scripts patch bump that should work with the current controller
  still requires the author to confirm the bracket's upper bound is
  still right. (Failure mode: forgot to widen the bracket → false
  conflict at restore.)
- A consumer who only wants scripts now also restores the controller
  DLL/module. The download cost is small for everything except
  Render (which would also pull blender/ffmpeg via
  `ResolveControllerAssetsTask`). Mitigation: if a future use case
  needs scripts without the controller, the PackageReference can be
  marked `<PrivateAssets>all</PrivateAssets>` so it's not transitive
  to the consumer's runtime — needs separate ADR if/when it comes
  up.

### Neutral

- Existing `Version="1.0.*"` floating refs in WitCloud keep working.
  The first time WitCloud is restored against a Scripts package that
  declares the new bracket dep, NuGet will pick a controller version
  inside the bracket overlapping WitCloud's own controller floating
  range. If both ranges overlap, the resolution is deterministic; if
  they don't, restore fails with a clear conflict.

## Alternatives considered

### Lockstep (Scripts version = Controller version)

Bump Render.Scripts to 1.15.0 when Render is 1.15.0, etc.

Rejected: forces a Scripts publish for every controller patch even
when no `.wit` file changed, which produces meaningless version
churn and a lot of identical packages on nuget.org. Also doesn't
actually prevent the failure mode (a consumer can still pin
mismatched versions).

### Compatibility table in README

A markdown table in each Scripts package README listing "Scripts
X.Y.Z is tested against Controller [Amin..Bmax]".

Rejected as the *primary* mechanism: tables in READMEs are
documentation, not enforcement. They depend on the consumer reading
and obeying them. The bracket constraint encodes the same intent
in a place NuGet actually checks. (We may still add a table for
human readability later.)

### Runtime gate in ScriptSeeder

Add a startup-time pre-check that walks every script's required
activities and verifies they exist in `engine.HostControllers` /
`engine.NodeControllers`. Fail the whole `ScriptSeeder.SeedAsync`
on first mismatch.

Rejected: blocks the whole cloud from starting because of a single
broken script. The current behavior — per-script `CompilationError`
persisted in `ScriptStore`, cloud still serves traffic, admin UI
flags the script — is preferable. The existing per-script
compilation already provides the diagnostic; a startup gate would
just make it worse.

### Do nothing (status quo)

Rely on cold builds + WitCloud's integration tests to catch
regressions late.

Rejected: tests run after restore, after build, possibly after
publish. The bracket constraint catches incompatible combinations
at restore — earlier and cheaper.

## Implementation

Each Scripts csproj gets the bracket-constrained PackageReference.
For the three packages currently published:

```xml
<!-- Matrices/OutWit.Controller.Matrices.Scripts/OutWit.Controller.Matrices.Scripts.csproj -->
<PackageReference Include="OutWit.Controller.Matrices" Version="[1.0.3, 2.0.0)" />

<!-- Render/OutWit.Controller.Render.Scripts/OutWit.Controller.Render.Scripts.csproj -->
<PackageReference Include="OutWit.Controller.Render" Version="[1.15.0, 2.0.0)" />

<!-- Render/OutWit.Controller.Render.Dcc.Scripts/OutWit.Controller.Render.Dcc.Scripts.csproj -->
<PackageReference Include="OutWit.Controller.Render.Dcc" Version="[1.0.3, 2.0.0)" />
```

Each lower bound matches the **earliest version actually published on
nuget.org** (Matrices' and Render.Dcc's pre-1.0.3 builds existed only
in local CI artifacts, never made it to a published release; Render's
1.15.0 was the first 1.15.x to ship). Setting the lower bound to a
non-existent version yields `NU1603` "package not found, X was
resolved instead" — the constraint still works but NuGet logs noise
on every restore.

A Scripts major bump (e.g. `1.0.0 → 2.0.0`) is always paired with
the corresponding controller's next major bracket. When Render moves
to 2.0.0, Render.Scripts publishes 2.0.0 with
`Version="[2.0.0, 3.0.0)"`.
