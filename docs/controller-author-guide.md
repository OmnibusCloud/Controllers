# Controller Author Guide

A practical guide to writing a new WitEngine controller — from `dotnet new` to a published NuGet package. Built around the six production controllers in this repo (Variables, Special, Grid, Matrices, Render.Dcc, Render); cites real file paths and line numbers so you can read the actual code alongside.

## What you'll need to know

This guide assumes you understand:

- The basics of [WitEngine concepts](../README.md#concepts) (controller, activity, adapter, model, Tier 1 vs Tier 2). Read the README first if those are new.
- C# 12 / .NET 10, NuGet, and MSBuild conventions (targets, items, item metadata).
- That the runtime is closed-source: a controller is a self-contained module loaded *by* the engine, never the other way round.

If you want to skim before reading: the [Reference](#reference) section is a flat field-by-field walk through every metadata knob; the [step-by-step walkthrough](#walkthrough-from-template-to-published-package) shows how the knobs fit together for a brand-new controller.

---

## The shape of a controller

Every controller in this repo is a **pair of NuGet packages** that ship together:

```
<Name>/
  OutWit.Controller.<Name>/                # logic: activities + adapters
    OutWit.Controller.<Name>.csproj
    WitController<Name>Module.cs           # plugin entry point — DI registrations
    Activities/                              # DTO classes (one per activity)
    Adapters/                                # executor classes (one per activity)
    Variables/                               # custom variable types (optional)
    Resources/                               # optional — auto-staged into <module>/Resources/
    build/
      OutWit.Controller.<Name>.targets       # consumer-side MSBuild — auto-imports
    LICENSE
    WitEngine-logo.png
    README.md

  OutWit.Controller.<Name>.Model/           # shared data types (if any)
    OutWit.Controller.<Name>.Model.csproj
    <Name>Data.cs                            # one class per DTO
    LICENSE
    WitEngine-logo.png
    README.md

  OutWit.Controller.<Name>.Tests/           # NUnit fixture per activity
    OutWit.Controller.<Name>.Tests.csproj
```

The `.Model` package is **optional** — Special doesn't have one because its control-flow activities don't cross the host↔node WitRPC boundary and there are no shared types worth their own nupkg. All the other production controllers carry one. Rule of thumb: split into a `.Model` if any DTOs **(a)** cross the host↔node boundary (must be MemoryPackable + self-contained) or **(b)** are useful to external tooling without taking the controller as a runtime dep.

At build time the shared `Build/` MSBuild logic stitches both projects into a single **module** directory that the WitEngine runtime loads:

```
@Controllers/<Cfg>/<name>.module/
  OutWit.Controller.<Name>.dll
  OutWit.Controller.<Name>.Model.dll
  controller.json                  # generated; see "The manifest" below
  Resources/...                     # optional, auto-staged from <csproj>/Resources/
  <ExtractTo>/...                   # optional, fetched by ResolveControllerAssetsTask
```

`<name>` is `<ControllerName>.ToLowerInvariant().module` — Variables → `variables.module`, Render → `render.module`, etc. ([`Build/OutWit.Controller.targets:16-21`](../Build/OutWit.Controller.targets#L16-L21)).

---

## Walkthrough: from template to published package

The fastest path is **copy an existing pair**:

| Want a... | Copy from |
|---|---|
| Tier-1 controller, no Model | `Special/` |
| Tier-1 controller with shared DTOs | `Variables/` or `Grid/` |
| Tier-2 controller with external assets | `Matrices/` (simple, two `.smat` files) or `Render/` (multi-platform native binaries) |
| Host-only controller (no worker nodes involved) | `Render/OutWit.Controller.Render.Dcc/` |

The walkthrough below assumes you've copied `Variables` and renamed it `Foo` — adapt as needed.

### 1. Rename the projects

```sh
# Project dirs and csprojs
mv Foo/OutWit.Controller.Variables Foo/OutWit.Controller.Foo
mv Foo/OutWit.Controller.Foo/OutWit.Controller.Variables.csproj \
   Foo/OutWit.Controller.Foo/OutWit.Controller.Foo.csproj
# (same for .Model and .Tests)

# Namespaces + class names — search-replace OutWit.Controller.Variables -> OutWit.Controller.Foo
```

### 2. Update csproj metadata

The shared `Build/OutWit.Controller.props` (auto-imported on line 1 of every controller csproj) handles target framework, license, icon, common PackageReferences, source linking, content-only pack shape — none of that needs touching. You only set the controller's identity:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="..\..\Build\OutWit.Controller.props" />

  <PropertyGroup>
    <ControllerName>Foo</ControllerName>     <!-- REQUIRED, PascalCase -->
    <Version>1.0.0</Version>                  <!-- REQUIRED, semver -->
    <Description>One-line summary.</Description>  <!-- REQUIRED -->
    <PackageTags>foo;witengine;witcloud;controller</PackageTags>
    <ControllerFeatures>foo-bar;foo-baz</ControllerFeatures>
    <ControllerUseCases>What this is for</ControllerUseCases>
  </PropertyGroup>

  <ItemGroup>
    <ControllerDependency Include="Variables" MinimumVersion="1.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\OutWit.Controller.Foo.Model\OutWit.Controller.Foo.Model.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Include="build\OutWit.Controller.Foo.targets" Pack="true" PackagePath="build\" />
  </ItemGroup>

  <Import Project="..\..\Build\OutWit.Controller.targets" />
</Project>
```

Three fields are validated at build time as a hard error: `ControllerName`, `Version`, `Description` ([`Build/OutWit.Controller.Manifest.targets:22-24`](../Build/OutWit.Controller.Manifest.targets#L22-L24)). Everything else is optional but most are surfaced in `controller.json` — see [The manifest](#the-manifest-controllerjson) for the full mapping.

### 3. Update the consumer-side targets file

Each controller ships a `build/OutWit.Controller.<Name>.targets` inside its nupkg that auto-imports when a downstream csproj references the package. Its job is to (a) copy the controller's `content/module/*` into the consumer's `bin/<Cfg>/<tfm>/@Controllers/<Cfg>/<name>.module/`, and (b) call `ResolveControllerAssetsTask` to fetch any external assets.

You copy [`Variables/OutWit.Controller.Variables/build/OutWit.Controller.Variables.targets`](../Variables/OutWit.Controller.Variables/build/OutWit.Controller.Variables.targets) and search-replace `Variables` → `Foo`. There's no shared base — every controller maintains its own. If you're writing a Tier-2 controller, copy from [`Render/OutWit.Controller.Render/build/OutWit.Controller.Render.targets`](../Render/OutWit.Controller.Render/build/OutWit.Controller.Render.targets) instead — it sets `ResolveForAllPlatforms="true"` so worker nodes get every RID's binaries, not just the host's.

### 4. Write the module class

This is the plugin entry point. The shared MSBuild logic auto-generates a tiny `ControllerBuildInfo.g.cs` ([`Manifest.targets:52-62`](../Build/OutWit.Controller.Manifest.targets#L52-L62)) with `NAME` and `VERSION` constants matching the manifest, so the `[WitPluginManifest]` attribute never drifts from `<Version>`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Controller.Foo.Activities;
using OutWit.Controller.Foo.Adapters;

namespace OutWit.Controller.Foo;

[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
public sealed class WitControllerFooModule : WitPluginBase, IWitControllerNode, IWitControllerHost
{
    public override void Initialize(IServiceCollection services)
    {
        services.AddActivityAdapter<WitActivityFooBar, WitActivityAdapterFooBar>();
        services.AddVariable<WitVariableFooThing>();
        services.AddResources<Resources>();   // only if you have Properties/Resources.resx
    }
}
```

Three interfaces decide where the controller runs:
- `IWitControllerHost` — runs on the WitCloud orchestrator (jobs, scheduling, scripts).
- `IWitControllerNode` — runs on worker nodes (executes activities).
- Both — most controllers; activities run on either side.

Render.Dcc is the canonical host-only example — it only implements `IWitControllerHost` (DCC scene validation runs upstream of distribution, not on workers).

**Registration is explicit.** There is no assembly-scan that auto-discovers `[Activity]`-marked classes. An activity not added to `Initialize` is invisible at runtime. The helper methods in [`OutWit.Engine.Data.Utils.ServicesUtils`](https://github.com/OmnibusCloud/Controllers/blob/main/) cover the common shapes:

```csharp
services.AddActivityAdapter<TActivity, TAdapter>();   // one per activity
services.AddVariable<TVariable>();                    // one per variable type
services.AddCollection<TCollection>();                // one per collection wrapper
services.AddResources<TResource>();                   // once per controller (if any)
```

### 5. Write your first activity

An activity is **two classes**: the DTO (serialised across the network) and the adapter (runs the work).

**DTO** — `Activities/WitActivityFooBar.cs`:

```csharp
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;

namespace OutWit.Controller.Foo.Activities;

[Activity("Foo.Bar")]      // script-level identifier
[MemoryPackable]            // wire-serialisable
public sealed partial class WitActivityFooBar : WitActivityFunction
{
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
        => modelBase is WitActivityFooBar;

    protected override WitActivityFooBar InnerClone() => new();
}
```

Pick the base class that matches the activity's calling convention:

| Base class | When to use | Production example |
|---|---|---|
| `WitActivityBase` (implicit) | Procedural activity — runs work, no return value. | `WitActivitySpecialBreak` |
| `WitActivityFunction` | Has a typed return value bound to a variable. | `WitActivityFooBar` above; `WitActivityRenderFrame` |
| `WitActivityCommand` | Side-effect-only command with operator semantics. | `WitActivitySpecialTrace` |
| `WitActivityComposite` | Contains child activities and a condition or pool (loops, conditionals). | `WitActivitySpecialIf`, `WitActivitySpecialForEach` |
| `WitActivityTransform` | Element-wise transform inside `Grid.ForEach`-style contexts. | adapters in `WitActivityAdapterTransform` users |

**Adapter** — `Adapters/WitActivityAdapterFooBar.cs`:

```csharp
using Microsoft.Extensions.Logging;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Controller.Foo.Activities;

namespace OutWit.Controller.Foo.Adapters;

internal sealed class WitActivityAdapterFooBar
    : WitActivityAdapterFunction<WitActivityFooBar>
{
    public WitActivityAdapterFooBar(
        IWitControllerManager controllerManager,
        IWitProcessingManager processingManager,
        ILogger logger)
        : base(controllerManager, processingManager, logger) { }

    protected override async Task<IWitProcessingStatus> ProcessInner(
        WitActivityFooBar activity,
        IWitVariablesCollection pool,
        IReadOnlyList<string> returnVariables,
        CancellationToken token)
    {
        // 1. pull inputs from `pool` (script-bound variables)
        // 2. run the work
        // 3. write outputs into `pool` for variables named in `returnVariables`
        return WitProcessingStatus.Success;
    }

    public override WitActivityFooBar CreateActivity(IWitParameter[] parameters)
    {
        // parse positional script parameters into the activity DTO
        return new WitActivityFooBar();
    }
}
```

The adapter base classes mirror the activity base classes — `WitActivityAdapterFunction` for `WitActivityFunction`, etc. — and bring in the standard inputs:
- `IWitControllerManager` — dispatches to other controllers' activities.
- `IWitProcessingManager` — schedules child jobs (for composites).
- `ILogger` — standard .NET logging.
- `IResources` — string table from the controller's `Properties/Resources.resx`, when present.

Adapter classes can be `internal` — they're only referenced from the module's `Initialize`, which is in the same assembly.

### 6. Write a Model DTO (if your activity needs one)

If your activity takes or returns structured data more complex than primitives, that data lives in the **`.Model`** project so the wire format is symmetric across host and node:

```csharp
// OutWit.Controller.Foo.Model/FooThingData.cs
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Foo.Model;

[MemoryPackable]
public sealed partial class FooThingData : ModelBase
{
    public string Name { get; set; } = null!;
    public int Count { get; set; }

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FooThingData other) return false;
        return Name.Is(other.Name) &amp;&amp; Count.Is(other.Count);
    }

    public override ModelBase Clone() => new FooThingData { Name = Name, Count = Count };
}
```

The four contracts every Model DTO honours:
1. Derive from `OutWit.Common.Abstract.ModelBase`.
2. `[MemoryPackable]` + `partial` (so the source generator can emit the serialiser).
3. Override `Is(ModelBase, double)` for value-comparison — used pervasively by the runtime for change detection and test asserts.
4. Override `Clone()` — used for the immutable `.With(x => x.Prop, value)` update pattern from `OutWit.Common`.

If the constructor takes arguments, decorate one of them `[MemoryPackConstructor]`. Computed properties get `[MemoryPackIgnore]`.

### 7. Wrap a Model DTO in a variable (optional)

If you want the DTO to be addressable from script syntax (`FooThing:result = Foo.Bar(...)`), wrap it:

```csharp
// OutWit.Controller.Foo/Variables/WitVariableFooThing.cs
[Variable("FooThing")]
[MemoryPackable]
public sealed partial class WitVariableFooThing
    : WitVariable<FooThingData?>, IWitVariableFactory<WitVariableFooThing>
{
    public WitVariableFooThing(string name) : base(name) { }

    [MemoryPackConstructor]
    public WitVariableFooThing(string name, FooThingData? value) : base(name, value) { }

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE) => ...;
    public override WitVariableFooThing Clone() => new(Name, Value);
    public static WitVariableFooThing Create(string name) => new(name);
}
```

Variables don't need an explicit adapter class — `services.AddVariable<T>()` registers a generic `WitVariableAdapter<T>` for you ([`ServicesUtils.cs:21-25`](https://github.com/OmnibusCloud/Controllers/blob/main/)).

### 8. Write a smoke test

Tests live in `OutWit.Controller.Foo.Tests/`. The csproj boilerplate is almost identical to Special's — see [section 7 of the Reference](#reference-testing). The minimum smoke test loads the SDK, compiles a job, runs it:

```csharp
using Microsoft.Extensions.DependencyInjection;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Foo.Tests.Activities;

[TestFixture]
public sealed class WitActivityFooBarTests
{
    [OneTimeSetUp]
    public void Setup() => WitEngineSdk.Instance.Reload(useIsolatedContext: false);

    [Test]
    public async Task FooBarReturnsValue()
    {
        var job = WitEngineSdk.Instance.Compile("""
            Job:Test()
            {
                FooThing:result = Foo.Bar();
            }
            """);

        var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
        Assert.That(job.Variables["result"], Is.Not.Null);
    }
}
```

Once that passes, you have a working controller. Continue with [Reference: Testing](#reference-testing) for the more involved patterns (distributed nodes, blob services, integration tests with external prereqs).

### 9. Wire into the solution + publish workflow

Add the three csprojs to [`OutWit.slnx`](../OutWit.slnx) under a new `<Folder Name="/Foo/">`. Add `OutWit.Controller.Foo` and `OutWit.Controller.Foo.Model` to the project-choice list in [`.github/workflows/publish.yml`](../.github/workflows/publish.yml#L28-L42).

For Path-A (first-party, nuget.org) distribution, the publish workflow handles everything. For Path-B (third-party), see the [`outwit-controller-pack` README](../Tools/OutWit.Controller.Pack/README.md).

---

## Reference

The walkthrough above sketches the shape. The sections below are reference material — each one a flat list of what's enforced where, with citations.

### Reference: the csproj metadata

Per-controller csproj sets identity and dependencies. Everything else comes from the shared imports. Properties:

| Property | Required? | Surfaced in `controller.json` as | Notes |
|---|---|---|---|
| `ControllerName` | yes | `name` | PascalCase. Also drives `<name>.ToLowerInvariant().module` directory. |
| `Version` | yes | `version` | Semver. Embedded in `[WitPluginManifest]` via the generated `ControllerBuildInfo.VERSION`. |
| `Description` | yes | `description` | One line. Note: no JSON escaping — a literal `"` will corrupt the manifest. |
| `Authors` | no | `authors[]` | Semicolon-separated; defaults to `Dmitry Ratner` from `Build/OutWit.Controller.props`. |
| `Copyright` | no | `copyright` | Defaults computed dynamically (year + author) in the shared props. |
| `PackageProjectUrl` | no | `projectUrl` | Defaults to `https://witengine.io/`. |
| `RepositoryUrl` | no | `repositoryUrl` | Defaults to `https://github.com/OmnibusCloud/Controllers`. |
| `PackageLicenseExpression` | no | `license` | Defaults to `MIT` (Build/OutWit.Controller.props:39). |
| `PackageTags` | no | `tags[]` | Semicolon-separated. Convention: include `witengine;witcloud;controller`. |
| `ControllerFeatures` | no | `features[]` | Semicolon-separated **stable capability ids**. Adding is fine; removing is a breaking change for downstream consumers that depend on a feature being present. |
| `ControllerUseCases` | no | `useCases[]` | Free-text. No stability contract. |
| `AssemblyName` | derived | `assemblyName` | Defaults to csproj filename. Used by the loader to find the entry DLL. |

Items:

| Item | What it controls |
|---|---|
| `<ControllerDependency Include="X" MinimumVersion="1.0.0" />` | A `dependencies` entry in `controller.json`. The resolver picks the highest installed version that satisfies `>= MinimumVersion`. |
| `<ControllerRuntimeTarget Include="X"><Rid>...</Rid><Os>...</Os><Architecture>...</Architecture></ControllerRuntimeTarget>` | A `runtimeTargets[]` entry. Lists which platforms the controller declares support for. Empty array means platform-agnostic (no native binaries). |
| `<ControllerDataAsset Include="id"><Uri>...</Uri><Sha256>...</Sha256>...</ControllerDataAsset>` | A `dataAssets[]` entry. Tier-2 controllers only — see [Resources & data assets](#reference-resources--data-assets). |
| `<None Include="build\OutWit.Controller.<Name>.targets" Pack="true" PackagePath="build\" />` | Required for the consumer-side build/.targets to ship inside the nupkg. |
| `<ProjectReference Include="..\<Name>.Model\....csproj" />` | Only if you have a Model project. Makes the Model DLL get co-staged into the module dir. |

What the shared `Build/OutWit.Controller.props` adds automatically ([`Build/OutWit.Controller.props:24-77`](../Build/OutWit.Controller.props)):
- `<TargetFramework>net10.0</TargetFramework>`, `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`.
- Content-only nupkg shape: `IncludeBuildOutput=false`, `NoPackageAnalysis=true`, hook into `TargetsForTfmSpecificContentInPackage` so `dotnet pack` produces a `content/module/` instead of a `lib/<tfm>/` package.
- `GeneratePackageOnBuild` on Release.
- Source linking (`PublishRepositoryUrl=true`, `EmbedUntrackedSources=true`).
- Standard PackageReferences (`OutWit.Engine.Data 1.1.*` floating, `OutWit.Engine.Assets.MSBuild 1.0.*` with `PrivateAssets=none; IncludeAssets=build;buildTransitive` — this is what wires `ResolveControllerAssetsTask` into every downstream consumer).
- Pack the per-csproj `LICENSE`, `WitEngine-logo.png`, `README.md` into the nupkg root. **These three files must exist next to every controller csproj** — they're declared with literal `Include="LICENSE"`, not `..\..\LICENSE`. Copying is mandatory.

### Reference: the Model csproj

Smaller — `Build/OutWit.Controller.Model.props` adds the same target framework and packs the same three local files, but Model is a normal `lib/<tfm>/` NuGet (not content-only). Minimum:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="..\..\Build\OutWit.Controller.Model.props" />

  <PropertyGroup>
    <ControllerName>Foo</ControllerName>
    <Version>1.0.0</Version>
    <Description>Shared data types for the Foo controller.</Description>
    <PackageTags>foo;witengine;witcloud;model</PackageTags>
  </PropertyGroup>

  <Import Project="..\..\Build\OutWit.Controller.Model.targets" />
</Project>
```

`Build/OutWit.Controller.Model.targets` post-build step copies the Model DLL into the parent controller's `.module/` directory ([`Build/OutWit.Controller.Model.targets:19-31`](../Build/OutWit.Controller.Model.targets#L19-L31)). This means **building the Model alone updates the staged module** — useful for incremental dev cycles.

Model package references (added by `Build/OutWit.Controller.Model.props`):
- `OutWit.Common 1.3.2` — `ModelBase`, value comparisons.
- `OutWit.Common.MemoryPack 1.1.4` — MemoryPack source generator.

### Reference: activities

Activity DTO contracts:
1. `[Activity("ScriptName")]` — the identifier the script grammar binds to (`Foo.Bar`, `Render.Frame`, `If`).
2. `[MemoryPackable]` and `partial` — for the source generator.
3. Derives from `WitActivityBase` or a more specific subclass (`Function`, `Command`, `Composite`, `Transform`).
4. Override `Is(ModelBase, double)` for value-equality.
5. Override `InnerClone()` to produce a copy with the same input state.
6. Properties carrying parameters: typed `IWitParameter?` (for inputs) or strongly-typed where the value is known at construction.

Adapter contracts:
1. Derives from `WitActivityAdapterBase<TActivity>` or a subclass matching the DTO's variant.
2. Constructor takes `IWitControllerManager`, `IWitProcessingManager`, `ILogger`, and optionally `IResources`.
3. Implements `ProcessInner(...)` — the execution method.
4. Implements `CreateActivity(IWitParameter[])` — parses positional script parameters into the DTO.
5. Registered exactly once via `services.AddActivityAdapter<TActivity, TAdapter>()` in the module's `Initialize`.

The script grammar binds positional parameters in DSL syntax (`Render.Frame(task)`) to whatever `CreateActivity` extracts. Variables in the script body are looked up at execution time from the `IWitVariablesCollection pool` passed to `ProcessInner`.

**Production examples to read** (all in this repo, all MIT-licensed):
- Smallest: [`Special/.../Activities/WitActivitySpecialBreak.cs`](../Special/OutWit.Controller.Special/Activities/WitActivitySpecialBreak.cs) + adapter — 30 lines each.
- Function with return: [`Render/.../Activities/WitActivityRenderFrame.cs`](../Render/OutWit.Controller.Render/Activities/WitActivityRenderFrame.cs).
- Composite with children + condition: [`Special/.../Activities/WitActivitySpecialIf.cs`](../Special/OutWit.Controller.Special/Activities/WitActivitySpecialIf.cs).
- Distributed-iteration composite: [`Grid/.../Activities/WitActivityGridForEach.cs`](../Grid/OutWit.Controller.Grid/Activities/WitActivityGridForEach.cs).

### Reference: variables

Variable wrapper contracts:
1. `[Variable("ScriptName")]` — the type name the script grammar uses (`Int`, `RenderResult`).
2. `[MemoryPackable]` and `partial`.
3. Derives from `WitVariable<TPayload>`.
4. Implements `IWitVariableFactory<TSelf>` — `public static TSelf Create(string name) => new(name)` so the framework can construct fresh empty instances by reflection.
5. Two constructors: `(string name)` for empty + `(string name, TPayload value)` decorated `[MemoryPackConstructor]`.
6. Override `Is` and `Clone`.

If `TPayload` is a Model DTO, the DTO itself must follow the [Model contracts](#6-write-a-model-dto-if-your-activity-needs-one). All wire-crossing data must be `[MemoryPackable]` — anything not registered with MemoryPack silently fails to deserialise on the receiving node.

Naming convention (across all six controllers):
- `WitVariable<Name>` — wrapper class (`WitVariableInteger`, `WitVariableRenderResult`).
- `WitVariable<Name>Collection` — collection wrapper (`WitVariableIntegerCollection`).
- `<Name>Data` — Model DTO inside `.Model` (`RenderResultData`).
- `<Name>Options` — option DTOs (`RenderOptionsData`, `TileOptionsData`).
- Plain `Wit<Name>` for primitive value types like [`WitColor`](../Variables/OutWit.Controller.Variables.Model/WitColor.cs) — no `Data` suffix.

### Reference: source folders

Across the production controllers, these folder names appear and have specific meanings. **Only `Resources/` is convention-auto-staged** by the shared targets — everything else is just organisation.

| Folder | What goes here | Auto-staged? |
|---|---|---|
| `Activities/` | Activity DTO classes (`WitActivity*`) | no |
| `Adapters/` | Adapter classes (`WitActivityAdapter*`) | no |
| `Variables/` | Variable wrapper classes (`WitVariable*`) | no |
| `Collections/` | Collection wrappers (`WitVariable*Collection`) | no |
| `Properties/Resources.resx` | String table — wired via `services.AddResources<Resources>()` | no (the resx is embedded into the assembly normally) |
| **`Resources/`** | **Data files staged into `<module>/Resources/`** — [`Build/OutWit.Controller.targets:56-68`](../Build/OutWit.Controller.targets#L56-L68) | **yes** |
| `Interfaces/` | Per-controller marker interfaces (Variables, Special use these) | no |
| `Utils/` | Helpers (`BlenderRunner.cs`, etc.) | no |
| `Services/` | Service classes (Render only) | no |
| `Builders/` | Builder pattern helpers (Grid only) | no |
| `Scripts/` | `.wit` script files (Render only — 36 of them) | no; staged by test csproj explicitly |
| `benchmarks/` | Python scripts that regenerate benchmark scenes (Render only) | no; author-side regeneration only |

### Reference: the manifest (`controller.json`)

Emitted at build time by [`Build/OutWit.Controller.Manifest.targets:64-85`](../Build/OutWit.Controller.Manifest.targets#L64-L85). Lives at three places by the end of a build:

1. `obj/<Cfg>/net10.0/controller.json` — the emitter writes here first.
2. `@Controllers/<Cfg>/<name>.module/controller.json` — copied by `ControllerPostBuild`.
3. `nupkg!/content/module/controller.json` — packed by `ControllerPackModuleContents`. At consumer build time the consumer-side build/.targets copies this back into the consumer's own `@Controllers/<Cfg>/<name>.module/`.

The full schema with field-by-field constraints lives at [`docs/controller.schema.json`](controller.schema.json). The build-time emitter only enforces five things:
- `ControllerName` not empty
- `Version` not empty
- `Description` not empty
- Every `ControllerDataAsset` has a non-empty `Uri`
- Every `ControllerDataAsset` has a non-empty `Sha256`

The schema's semver pattern, 64-hex-char SHA pattern, RID enum, etc. are *documentation only* — there is no JSON Schema validation step in the build. You can verify the schema against your built manifest by hand:

```sh
pip install jsonschema
python -c "import json, jsonschema; \
  schema = json.load(open('docs/controller.schema.json')); \
  data = json.load(open('@Controllers/Debug/foo.module/controller.json')); \
  jsonschema.validate(instance=data, schema=schema)"
```

If your CI for downstream consumers needs strict validation, run this in a check.

There's a subtle stale-artifact: some controllers carry a `controller.generated.json` file checked into source (e.g. [`Special/.../controller.generated.json`](../Special/OutWit.Controller.Special/controller.generated.json)). These are pre-refactor snapshots and are not read by anything. Safe to delete; until they're cleaned up, ignore them when reading source.

### Reference: resources & data assets

There are two distinct mechanisms for shipping non-code assets with a controller.

#### `Resources/` (in-source, in-nupkg-excluded, auto-staged)

Anything under `<csproj>/Resources/**` is copied into `<module>/Resources/` at build time by [`Build/OutWit.Controller.targets:56-68`](../Build/OutWit.Controller.targets#L56-L68). Used for small reference data that should be in the source tree.

Critical wrinkle: `ControllerPackModuleContents` ([`Build/OutWit.Controller.targets:84-109`](../Build/OutWit.Controller.targets#L84-L109)) **excludes `Resources/`** from the nupkg. This means consumers do *not* get the bytes inline. For Tier-1 controllers with `Resources/` (e.g. Variables has none), runtime lookup must walk up to find the dev-side `@Controllers/` directory — which works locally but fails in a NuGet-only consumer install.

Tier-2 controllers solve this by **also** declaring those same files as `ControllerDataAsset` entries with a `gh-release://` Uri. Local dev / in-solution tests use the auto-staging path; published consumers use the asset resolver. Matrices is the small canonical example — see [`Matrices/OutWit.Controller.Matrices/OutWit.Controller.Matrices.csproj`](../Matrices/OutWit.Controller.Matrices/OutWit.Controller.Matrices.csproj).

#### `ControllerDataAsset` (external, content-addressed, fetched at consumer build)

For binaries and large assets that don't belong in source control or NuGet feeds. Each entry maps a stable id to a fetchable Uri + SHA256 verification + an extract destination inside the module:

```xml
<ItemGroup>
  <ControllerDataAsset Include="blender-win-x64">
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <Uri>gh-release://OmnibusCloud/Controllers/render-v1.15.2/blender-windows-x64.zip</Uri>
    <Sha256>8d97449730a5cf6958e38738665252f4aba65abfa448f77de012e207e1e3344a</Sha256>
    <Size>429597391</Size>
    <ExtractTo>blender/windows-x64/</ExtractTo>
    <PackSource>blender/windows-x64</PackSource>
    <PackKind>zip-folder</PackKind>
  </ControllerDataAsset>
</ItemGroup>
```

The fields:
- `Identity` (the `Include="..."` value) — stable id for diagnostics and disambiguation across platforms.
- `RuntimeIdentifier` — defaults to `any` (every host fetches). Specific RID gates to that platform.
- `Uri` — required. Built-in schemes: `gh-release://`, `https://`, `file://`. Add your own resolver via the `OutWit.Engine.Assets` interfaces.
- `Sha256` — required. 64 hex chars. The `outwit-assets-pack` tool fills this in automatically; mismatch aborts fetch.
- `Size` — informational, drives progress reporting.
- `ExtractTo` — defaults to `.` (module root). `.zip` files get extracted into the path; everything else is copied verbatim.
- `Required` — defaults to `true`. When `false`, fetch failures are tolerated (used for optional benchmark fixtures).
- `PackSource` and `PackKind` — **author-only**; these are stripped before manifest emission and only used by the `outwit-assets-pack` tool to know what to package.

`outwit-assets-pack` regenerates a Tier-2 controller's asset set in one command: bumps version, repacks zips, computes SHAs, updates the csproj, creates the GitHub Release with everything uploaded. See [its README](../Tools/OutWit.Controller.Assets.Pack/README.md).

**GitHub Release tag convention**: the publish workflow ([`.github/workflows/publish.yml:107-108`](../.github/workflows/publish.yml#L107-L108)) computes the tag as `<ControllerName.lower-no-dots>-v<Version>`:
- `OutWit.Controller.Render` v1.15.2 → tag `render-v1.15.2`
- `OutWit.Controller.Render.Dcc` v1.0.3 → tag `renderdcc-v1.0.3`

The tag is **hand-written into each `<ControllerDataAsset Uri>`**. When bumping `<Version>` manually, you must also update every Uri. Tools like `outwit-assets-pack --version` handle the rewrite; bare version bumps drift silently.

#### Built-in URI schemes

Resolution happens at consumer build time via `ResolveControllerAssetsTask`. The composite chain checks:

| Scheme | Resolver | Notes |
|---|---|---|
| `gh-release://owner/repo/tag/file` | `GitHubReleaseResolver` | Public releases need no auth. Translated to `https://github.com/owner/repo/releases/download/tag/file`. |
| `https://...` / `http://` | `HttpResolver` | |
| `file://...` | `LocalFileResolver` | Used by `outwit-controller-pack` Path-B zips. |

Third-party schemes can be added by passing extra `IAssetResolver` instances to the engine's asset infrastructure.

#### Cache

`ResolveControllerAssetsTask` writes to a content-addressed cache at `~/.outwit/asset-cache/` keyed by `SHA256 + filename`. First-time fetch downloads + verifies + stores; subsequent builds in any project re-use the cached bytes. The cache survives across machines if you tar+restore it.

### Reference: testing

Three SDK setup patterns in production:

**1. Single-engine, no nodes** — Special's pattern. The simplest, used for activities that don't involve distributed execution:

```csharp
[OneTimeSetUp]
public void Setup() => WitEngineSdk.Instance.Reload(useIsolatedContext: false);
```

**2. Single-engine + explicit module folder** — Render's lighter fixtures. Needed when the test must point at a specific `@Controllers/` location (e.g. because `AppContext.BaseDirectory` doesn't have one nearby):

```csharp
[OneTimeSetUp]
public void Setup()
{
    var controllersPath = FindControllersPath()
                          ?? throw new DirectoryNotFoundException("@Controllers directory not found");
    WitEngineSdk.Instance.Reload(false, null, controllersPath);
}
```

**3. Single-engine + IWitBlobService injection** — Render's Pattern-2 fixtures. When the controller needs an `IWitBlobService` (for example, the activity uploads blobs to a storage backend), inject a mock through the `configureServices` callback:

```csharp
[SetUp]
public void SetUp()
{
    m_blobService = new RenderTestBlobService(m_storageDir);
    var controllersPath = RenderTestAssetPaths.FindControllersPath()!;

    m_engine = WitEngineSdk.Instance;
    m_engine.Reload(
        useIsolatedContext: false,
        logger: null,
        moduleFolder: controllersPath,
        configureServices: services => services.AddSingleton<IWitBlobService>(m_blobService));
}
```

This is the overload added in `OutWit.Engine.Sdk 1.1.3` — earlier versions of the SDK don't accept `configureServices`.

**4. Host + worker node (distributed)** — Grid's pattern. The SDK supports running a "host" engine and a "node" engine in the same process via separate `WitEngineSdk.Instance` and `WitEngineNodeSdk.Instance` singletons. The host's `IWitNodesManager` is what dispatches activities to the node:

```csharp
[SetUp]
public void SetUp()
{
    WitEngineNodeSdk.Instance.Reload(
        useIsolatedContext: false,
        moduleFolder: m_controllersPath,
        configureServices: services => services.AddSingleton<IWitBlobService>(m_blobService));

    m_engine = WitEngineSdk.Instance;
    m_engine.Reload(
        useIsolatedContext: false,
        logger: null,
        moduleFolder: m_controllersPath,
        configureServices: services =>
        {
            services.AddSingleton<IWitBlobService>(m_blobService);
            services.AddSingleton<IWitNodesManager>(new RenderTestNodesManager(WitEngineNodeSdk.Instance));
        });
}
```

This is the pattern any controller with distributed activities must use to validate end-to-end. See the [Grid tests](../Grid/OutWit.Controller.Grid.Tests/) for the mock `IWitNodesManager` implementation.

#### Test csproj boilerplate

Test csprojs import [`Build/OutWit.Controller.Tests.props`](../Build/OutWit.Controller.Tests.props), which carries the standard SDK boilerplate (target framework, nullable, IsPackable=false, IsTestProject=true), the common test PackageReferences (Microsoft.NET.Test.Sdk, NUnit suite, OutWit.Common.NUnit, OutWit.Engine.Sdk), and the staging target that copies the populated `$(SolutionDir)@Controllers/<Cfg>/` tree into the test bin's `@Controllers/`. A minimal test csproj looks like:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <Import Project="..\..\Build\OutWit.Controller.Tests.props" />

  <ItemGroup>
    <ProjectReference Include="..\OutWit.Controller.Foo\OutWit.Controller.Foo.csproj" />
  </ItemGroup>

  <!-- Build-only references to every controller this test's runtime needs (each
       [WitPluginDependency] on the controller under test plus any controller
       its bundled scripts dispatch into via Grid.ForEach etc.).
       ReferenceOutputAssembly=false means MSBuild builds these projects (so their
       ControllerPostBuild populates @Controllers/<Cfg>/<name>.module/) but does
       not pull their DLLs into the test project's compile output. -->
  <ItemGroup>
    <ProjectReference Include="..\..\Variables\OutWit.Controller.Variables\OutWit.Controller.Variables.csproj"
                      ReferenceOutputAssembly="false" Private="false" />
  </ItemGroup>

  <!-- Optional: skip specific modules during the test-bin staging copy if a
       prior `dotnet build OutWit.slnx` populated heavy modules you don't need
       in this test. Item-driven so the shared target honors it. -->
  <ItemGroup>
    <ExcludedControllerModule Include="render.module" />
    <ExcludedControllerModule Include="renderdcc.module" />
  </ItemGroup>

</Project>
```

Two patterns to keep in mind:

- **The build-only ProjectReferences (`ReferenceOutputAssembly="false"`) are load-bearing.** They make the cross-controller build dependency explicit at the MSBuild level. Without them, `dotnet test ./X.Tests.csproj` (without a prior `dotnet build OutWit.slnx`) would only build the controller-under-test — sibling modules wouldn't be populated in `@Controllers/<Cfg>/`, the staging target would copy nothing, and the test runtime would fail with "unresolved dependency". With them, MSBuild builds each sibling controller's `ControllerPostBuild` before the staging target runs.

- **`ExcludedControllerModule` items drop specific modules** from the test-bin staging copy. Tier-1 tests typically exclude `render.module` and `renderdcc.module` so a developer who ran `dotnet build OutWit.slnx` first doesn't drag the heavyweight native runtimes into the lean test bin. The items don't prevent the sibling builds (those are gated by ProjectReferences), they only filter the final staging step.

Render.Dcc.Model.Tests (a pure data-model test) and the `Tools/*.Tests` projects do NOT import `OutWit.Controller.Tests.props` — they don't need controller staging at all.

#### Asset-path resolvers

Render carries a typed [`RenderTestAssetPaths.cs`](../Render/OutWit.Controller.Render.Tests/Utils/RenderTestAssetPaths.cs) helper with `FindControllersPath()`, `FindSolutionRoot()`, `FindBundledScriptsPath()`, and per-asset getters. Other controllers re-derive these inline. There's no shared `OutWit.Controller.Tests.Common` library; copy `RenderTestAssetPaths.cs` if you need similar helpers.

#### Marking tests as opt-in

Use `[Explicit("reason")]` on the fixture (or test method) for anything that needs prerequisites not always available — external native binaries, fixture scene files, environment variables, etc. Default `dotnet test` runs skip `[Explicit]`; CI or a local dev with everything in place opts in via `--filter "FullyQualifiedName~..."`.

For golden-file comparison patterns, set `WIT_RENDER_UPDATE_GOLDENS=1` to generate the baseline on first run; subsequent runs assert against it. Note that real Blender renders are non-deterministic at the pixel level, so golden-asserting tests are inherently flakier than hash-comparison.

### Reference: the build pipeline

The shared MSBuild logic runs in this order at every controller build:

1. **`Build/OutWit.Controller.props`** imported at the top of the csproj. Sets up identity defaults, common PackageReferences, content-only nupkg shape.
2. **`Build/OutWit.Controller.targets`** imported at the bottom. Pulls in `OutWit.Controller.Manifest.targets`. Computes `ControllerModuleName`, `ControllerModuleOutputDir`, `ControllerZipOutputDir` from `ControllerName` and `Configuration`.
3. **`GenerateControllerBuildArtifacts`** (BeforeTargets=`BeforeCompile`) — validates the five required fields, emits `obj/<Cfg>/net10.0/ControllerBuildInfo.g.cs` and `obj/<Cfg>/net10.0/controller.json`, then adds the .cs to `<Compile>`.
4. **`ControllerPostBuild`** (AfterTargets=`PostBuildEvent`) — copies the controller's DLL + `.deps.json` + `controller.json` + any locale-resource DLLs into `$(SolutionDir)@Controllers/$(Configuration)/<name>.module/`.
5. **`ControllerStageSourceResources`** (AfterTargets=`ControllerPostBuild`) — copies `<csproj>/Resources/**` into `<module>/Resources/`. Skipped silently when the folder doesn't exist.
6. **Companion `ModelPostBuild`** in the `.Model` csproj — copies the Model DLL into the SAME `<module>` directory.
7. **`ControllerPackZip`** (AfterTargets=`ControllerPostBuild`) — zips `$(ControllerModuleOutputDir)` into `$(SolutionDir)@Zips/$(Configuration)/<name>.zip`. The zip is used by the WitCloud server's BlobCacheService.
8. **`ControllerPackModuleContents`** (runs during `dotnet pack`) — packages the staged module (minus `Resources/`) into the nupkg's `content/module/` + writes an empty `lib/<tfm>/_._` marker so NuGet considers the package framework-compatible.

Final on-disk layout after a Release build of the full solution:

```
<repo>/@Controllers/Release/
  variables.module/
    OutWit.Controller.Variables.dll
    OutWit.Controller.Variables.Model.dll
    OutWit.Controller.Variables.deps.json
    controller.json
  grid.module/...
  render.module/
    OutWit.Controller.Render.dll
    OutWit.Controller.Render.Model.dll
    controller.json
    benchmark_scene.blend       # Debug-only: staged from @Prerequisites for tests
    benchmark_scene_still.blend
    benchmark_scene_video.blend
    # Note: blender/ and ffmpeg/ subfolders are NOT here in the source tree —
    # they're materialised in CONSUMER builds via ResolveControllerAssetsTask
    # when a downstream csproj PackageReferences OutWit.Controller.Render.

<repo>/@Zips/Release/
  variables.zip
  grid.zip
  ...
```

And in the nupkg:

```
OutWit.Controller.Render.<version>.nupkg
├── content/module/
│   ├── OutWit.Controller.Render.dll
│   ├── OutWit.Controller.Render.Model.dll
│   ├── controller.json
│   └── ...
├── build/
│   └── OutWit.Controller.Render.targets    # auto-imported in consumer
├── lib/net10.0/_._                         # framework-compat marker
├── LICENSE
├── WitEngine-logo.png
└── README.md
```

### Reference: the publish workflow

`.github/workflows/publish.yml` is a unified workflow for every nupkg in the repo (controllers, models, tools, the `Engine.Assets.MSBuild`). It takes a `workflow_dispatch` input with the project name and:

1. **Tier-2 only**: HEAD-checks every expected GitHub Release asset before pushing the nupkg to nuget.org — refuses to publish if assets are missing. The asset list is currently hard-coded for Render in the workflow ([`publish.yml:237-246`](../.github/workflows/publish.yml#L237-L246)). If you add a new Tier-2 controller, the workflow needs a corresponding asset-existence guard.
2. Builds in Release, packs the nupkg.
3. Pushes to nuget.org with `pushToNuGet: true` (default).
4. Pushes to GitHub Packages with `pushToGitHubPackages: true` (default).
5. Creates the GitHub Release tag (`<name.lower-no-dots>-v<version>`) with `createGitHubRelease: true`.

For Path-A distribution this is fully automated. For Path-B (third-party authors without nuget.org rights), use [`outwit-controller-pack`](../Tools/OutWit.Controller.Pack/README.md) to produce a self-contained zip that gets uploaded through the WitCloud admin UI.

---

## Sharp edges to know about

A list of things that "just work by magic" or have known asymmetries. Workarounds exist but the gotchas are real:

- **Manifest emission is string-template-based with no JSON escaping** ([`Manifest.targets:64-85`](../Build/OutWit.Controller.Manifest.targets#L64-L85)). A `<Description>` containing a literal `"` corrupts the manifest. Workaround: use `&quot;` in the csproj.
- **The schema in [`docs/controller.schema.json`](controller.schema.json) is documentation only** — no JSON Schema validation step runs at build. If you want strict checks, run jsonschema against your built manifest in CI.
- **GitHub Release tags are hand-written into every `<ControllerDataAsset Uri>`** for Tier-2 controllers. Bumping `<Version>` manually drifts the URIs silently. Use `outwit-assets-pack --version X.Y.Z --apply` to rewrite in one shot, or rely on the publish workflow's asset-existence guard to catch you before publish.
- **`AssetExtractor` has no path-traversal guard** — a malicious `<ExtractTo>` of `../../something` would land outside the module dir. Only trust controllers from sources you trust.
- **Per-controller `build/<id>.targets` is copy-paste boilerplate** — no shared base. Tier-1 differs from Tier-2 only by `ResolveForAllPlatforms="true"`. Copy from `Variables` or `Render` depending.
- **Runtime resource resolution is hand-rolled per controller.** Render's [`RenderBinaryResolver`](../Render/OutWit.Controller.Render/Utils/RenderBinaryResolver.cs) walks `assembly-dir/<sub>` then `@Controllers/Debug/<name>.module/<sub>` then `@Prerequisites/<sub>`. Other controllers re-invent similar walkers. There is no shared `IControllerResourceResolver` to import.
- **Platform-folder magic strings.** `windows-x64`/`linux-x64`/`macos-arm64` (used in `<ExtractTo>` and `RenderBinaryResolver`) differ from .NET RID names (`win-x64`/`linux-x64`/`osx-arm64`) used in `<RuntimeIdentifier>`. The mapping is duplicated in resolver code; no enforcement.
- **`features[]`** is treated as a public-API surface (additions are fine; removals are breaking) but is just a semicolon-split string with no tooling to detect drift. If consumers depend on a feature being present, you'll only find out when they break.
- **`OutWit.Engine.Assets` (open-source) only reads `name`/`version`/`dataAssets[]` from the manifest.** Every other field is parsed-and-ignored. Consistency checks for `dependencies` or `runtimeTargets` would have to live in the closed `OutWit.Cloud.Data` side.
- **The `[Activity]` registration is explicit, not scan-based.** An activity not added in `Initialize` is invisible at runtime. There's no "you forgot to register this" error — the script just fails to parse with an unknown-activity error.

---

## Reading the codebase

The fastest way to get fluent is to read existing controllers in this order:

1. [`Special/`](../Special/) — smallest, no Model, ~15 activities. Read [`WitControllerSpecialModule.cs`](../Special/OutWit.Controller.Special/WitControllerSpecialModule.cs), [`WitActivitySpecialBreak.cs`](../Special/OutWit.Controller.Special/Activities/WitActivitySpecialBreak.cs) + adapter, [`WitActivitySpecialIf.cs`](../Special/OutWit.Controller.Special/Activities/WitActivitySpecialIf.cs) + adapter (composite pattern).
2. [`Variables/`](../Variables/) — adds the Model project, lots of variable types. Skim [`WitControllerVariablesModule.cs`](../Variables/OutWit.Controller.Variables/WitControllerVariablesModule.cs) to see DI for variables; read [`WitVariableInteger.cs`](../Variables/OutWit.Controller.Variables/Variables/WitVariableInteger.cs) and [`WitColor.cs`](../Variables/OutWit.Controller.Variables.Model/WitColor.cs).
3. [`Grid/`](../Grid/) — distributed iteration. Look at [`WitActivityGridForEach.cs`](../Grid/OutWit.Controller.Grid/Activities/WitActivityGridForEach.cs) and the tests' `MockNodesManager`.
4. [`Matrices/`](../Matrices/) — first taste of Tier-2 with two `.smat` files. Read the csproj's `<ControllerDataAsset>` entries; small enough to grok in one pass.
5. [`Render/`](../Render/) — the heavyweight Tier-2 example with multi-platform native binaries. Skim the csproj's asset list; read [`RenderBinaryResolver.cs`](../Render/OutWit.Controller.Render/Utils/RenderBinaryResolver.cs) for the runtime resolution pattern.

The shared `Build/` targets are short and worth reading once:
- [`OutWit.Controller.props`](../Build/OutWit.Controller.props) — ~80 lines, declarative.
- [`OutWit.Controller.targets`](../Build/OutWit.Controller.targets) — ~110 lines, post-build + pack.
- [`OutWit.Controller.Manifest.targets`](../Build/OutWit.Controller.Manifest.targets) — ~90 lines, manifest + ControllerBuildInfo emission.
- [`OutWit.Controller.Model.props`](../Build/OutWit.Controller.Model.props) + [`OutWit.Controller.Model.targets`](../Build/OutWit.Controller.Model.targets) — even smaller.

After that, the tools READMEs are useful when you actually go to publish:
- [`Tools/OutWit.Controller.Assets.Pack/README.md`](../Tools/OutWit.Controller.Assets.Pack/README.md) — Tier-2 asset packaging.
- [`Tools/OutWit.Controller.Pack/README.md`](../Tools/OutWit.Controller.Pack/README.md) — Path-B contributor zips.

---

## Open questions / future work

This guide describes the current shape. A few things are stable but the surrounding tooling is still evolving:

- A shared `OutWit.Controller.Tests.Common` library carrying `FindControllersPath` / `FindSolutionRoot` / SDK setup helpers.
- A shared `OutWit.Controller.Consumer.targets` template referenced from each per-controller `build/<id>.targets` to eliminate ~40 lines of boilerplate per controller.
- JSON Schema validation as an MSBuild step against [`docs/controller.schema.json`](controller.schema.json) — would catch typos in `<Description>` etc. before they hit `controller.json`.
- A `[Job]` activity attribute is defined (`OutWit.Engine.Data.Attributes.JobAttribute`) but no production controller uses it directly — Jobs are currently declared inline in `.wit` script syntax. If a future use surfaces, this guide should grow a section on it.

Contributions to any of the above are welcome.
