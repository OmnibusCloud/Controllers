# OpenFOAM: supported inputs

What the OpenFOAM controller runs and what it refuses, as the rules in
`OutWit.Controller.OpenFOAM.Model` (`Rules/`) state them. The node, the Sweep
host and an initiator's preflight apply the same rules and refuse with the same
sentences; the tables below are held to those constants by the controller's
tests, so a rule that changes cannot leave this page behind.

## The build

Every case runs on **OpenFOAM v2606**, whole, on one node, from the pinned kit
of [OmnibusCloud/OpenFOAM](https://github.com/OmnibusCloud/OpenFOAM) (release
`openfoam-v2606-3`). There is no conversion and no other build: a case written
for another OpenFOAM line or version runs only if v2606 accepts it.

| Platform | Kit | Notes |
|---|---|---|
| Linux x64 | `linux-x64` | built on Ubuntu 22.04: glibc 2.35 or newer |
| macOS arm64 | `osx-arm64` | Apple Silicon |
| Windows x64 | `win-x64` | a parallel step runs under MS-MPI when the node has it installed, serially otherwise |

A node takes a case when it has at least 4 GB of RAM and 4 GB of free temporary
storage. A parallel step runs on the ranks the task asks for; "all cores" means
at most 16 ranks. The controller writes the `decomposeParDict` for that count
(scotch); a `decomposeParDict` the case carries is not used.

## The case

A case is a **reconstructed** case directory: `system/controlDict` with an
`application` entry, the time directories at the root (WitSweep uploads a case's
`0.orig/` as `0/` when it has no `0/`), and either a mesh in `constant/polyMesh`
or a step that makes one. Every file travels as it is: the node substitutes a
study's tokens byte for byte, adds the function objects the responses need and,
for a parallel run, writes its own `decomposeParDict`. One more change, only
when a recipe asks for it (the `includeFunc` step below): one line,
`#includeFunc <response>`, in the `functions` of the node's copy of
`system/controlDict`, so that the solver measures a force while it solves. The
user's own files are never changed; nothing else changes in the copy.

A case file path is refused when it

- uses backslashes, contains whitespace, has empty or `.` segments, or leaves
  the case directory;
- is a log of an earlier run (`log.*` at the root);
- lies under `postProcessing/` of an earlier run (its values would pass for this
  run's);
- lies under `processor*/` - a decomposed case is not accepted, submit the
  reconstructed case;
- repeats another path, or differs from one by letter case alone (one file on a
  Windows or macOS node).

A case is refused, by file and line, when it carries code that would be compiled
while it runs - the kit ships no compiler, and a pool never runs code it cannot
name:

| Construct | Refused because |
|---|---|
| `codeStream`, `#codeStream` | compiles C++ at run time |
| `#calc` | compiles C++ at run time (`#eval` is the in-built evaluator and is allowed) |
| a `coded` boundary condition or function object | compiles C++ at run time |
| `dynamicCode/` | compiled run-time code the case carries |
| `libs (...)` naming a library the kit does not have | the library is not there |
| `#include`, `#includeIfPresent`, `#includeFunc`, `#includeEtc` reaching outside the case | the run must be reproducible from the case alone |
| a UTF-8 byte order mark at the start of a file | OpenFOAM reads it as part of the first word |

Files larger than 8 MB (meshes, large fields) are not read for these
constructs.

## The recipe

A recipe is an ordered list of steps, at most **32**, one of which runs the
case's application - the solver its `controlDict` names. A step is a utility of
the allow-list, a solver, or a step the controller does itself. There is no
shell step and no custom command.

**Solvers** are the kit's executables whose name ends in `Foam` (`simpleFoam`,
`interFoam`, `rhoPimpleFoam`, ...) other than the utilities below
(`potentialFoam` is a utility); every solver may run in parallel.

### Utilities

| Utility | Parallel |
|---|---|
| `blockMesh` | |
| `surfaceFeatureExtract` | |
| `snappyHexMesh` | yes |
| `extrudeMesh` | |
| `refineMesh` | yes |
| `setFields` | yes |
| `mapFields` | |
| `topoSet` | yes |
| `createPatch` | yes |
| `createBaffles` | yes |
| `transformPoints` | |
| `renumberMesh` | yes |
| `checkMesh` | yes |
| `decomposePar` | |
| `reconstructPar` | |
| `reconstructParMesh` | |
| `postProcess` | yes |
| `foamDictionary` | |
| `potentialFoam` | yes |
| `setsToZones` | |
| `subsetMesh` | |
| `splitMeshRegions` | yes |
| `mergeMeshes` | |
| `mergeOrSplitBaffles` | |
| `extrudeToRegionMesh` | |
| `refineHexMesh` | |
| `collapseEdges` | |
| `extrude2DMesh` | |
| `setAlphaField` | |
| `makeFaMesh` | yes |

A parallel step naming a utility not marked here is refused.

### Built-in steps

- `restore0Dir -processor` - puts the initial fields into every processor
  directory after `decomposePar` (what a case meshed in parallel needs). Its only
  form; it needs a `decomposePar` step before it and never runs under MPI.
- `includeFunc <response>` - adds one of the task's responses to the solve: the
  line `#includeFunc <response>` at the end of the `functions` of the node's copy
  of `system/controlDict` (a `functions` block of its own when the file has
  none), and a `log.includeFunc` that says what was added, where and why. Why:
  a force measured after the solve (`<solver> -postProcess`) sees the walls a
  rotating zone (MRF) turns at rest, because OpenFOAM moves them only inside the
  solve - the torque comes out wrong by an order of magnitude or more, even in
  sign; measured by the solver, a force is the solve's own. It names one
  response of the task, it needs a step after it that runs the application to
  solve, and it never runs under MPI. When the `functions` entry is not a block
  (`functions #includeEtc "..."`), the step fails with that line and leaves the
  copy as it was, rather than rewrite it.

### Arguments

A step's arguments are flags (`-overwrite`, `-latestTime`, ...) and plain values.
No argument may point outside the case, and `-parallel` is never an argument -
it is the step's parallel switch. These flags are decided by the controller and
refused in a recipe:

`-case`, `-decomposeParDict`, `-fileHandler`, `-hostRoots`, `-roots`, `-lib`, `-libs`

## Responses

A response is a number the node reads from what OpenFOAM writes, by a function
object the case already runs or one the controller adds for the run:

| Kind | Function object | Needs |
|---|---|---|
| `ForceCoeffs` | `forceCoeffs` | at least one patch; the reference values (`magUInf`, `lRef`, `Aref`, ...) as parameters |
| `Forces` | `forces` | at least one patch |
| `PatchValue` | `surfaceFieldValue` | exactly one patch, at least one field, an operation (`areaAverage`, `areaIntegrate`, `min`, `max`, ...) |
| `VolumeValue` | `volFieldValue` | at least one field, an operation (`volAverage`, `volIntegrate`, `min`, `max`, ...) |
| `FieldMinMax` | `fieldMinMax` | at least one field |
| `Probe` | `probes` | at least one field, a `probeLocations` parameter |

A response's name is a word, unique in the request, and not the name of a file
the case carries in `system/`. A patch is a word or a quoted regular
expression; a field is a word that may carry dots, as phases are named
(`alpha.water`) - never a colon, since a probe writes one file per field. A
response the case does not write itself is measured either during the solve
(`includeFunc`, which a force on a turning wall needs) or after it by a post
step. A value is reported only from the run's final time: a function object
that stopped writing earlier is left out and named in `log.responses`. In a parameter study a response parameter may carry the
study's token (a reference speed that follows the swept speed).

## Solver classes

The class of a solver prices a run for the scheduler and, in WitSweep, estimates
its memory from measured curves. A solver outside the table has no class: it
runs, priced as the unknown class.

| Class | Solvers |
|---|---|
| `incompressible-steady` | `simpleFoam`, `porousSimpleFoam`, `SRFSimpleFoam`, `overSimpleFoam`, `boundaryFoam`, `adjointOptimisationFoam`, `adjointShapeOptimizationFoam` |
| `incompressible-transient` | `pimpleFoam`, `pisoFoam`, `icoFoam`, `nonNewtonianIcoFoam`, `SRFPimpleFoam`, `overPimpleDyMFoam`, `shallowWaterFoam` |
| `compressible-steady` | `rhoSimpleFoam`, `rhoPorousSimpleFoam`, `overRhoSimpleFoam` |
| `compressible-transient` | `rhoPimpleFoam`, `rhoCentralFoam`, `rhoPimpleAdiabaticFoam`, `overRhoPimpleDyMFoam`, `sonicFoam`, `sonicDyMFoam`, `sonicLiquidFoam` |
| `thermal-steady` | `buoyantSimpleFoam`, `buoyantBoussinesqSimpleFoam`, `chtMultiRegionSimpleFoam` |
| `thermal-transient` | `buoyantPimpleFoam`, `buoyantBoussinesqPimpleFoam`, `overBuoyantPimpleDyMFoam`, `chtMultiRegionFoam`, `laplacianFoam`, `solidFoam` |
| `multiphase-transient` | `interFoam`, `interIsoFoam`, `interMixingFoam`, `overInterDyMFoam`, `interPhaseChangeFoam`, `interPhaseChangeDyMFoam`, `compressibleInterFoam`, `compressibleInterIsoFoam`, `compressibleInterDyMFoam`, `multiphaseInterFoam`, `multiphaseEulerFoam`, `twoPhaseEulerFoam`, `reactingTwoPhaseEulerFoam`, `reactingMultiphaseEulerFoam`, `driftFluxFoam`, `cavitatingFoam`, `twoLiquidMixingFoam`, `potentialFreeSurfaceFoam` |

## What comes back

Every run returns its facts - converged or not, iterations, final residuals,
cells, `checkMesh`'s verdict, the time of each step, exit code, warnings - and
its responses. What else comes back is the task's artifact policy: nothing, the
final time and the mesh, or every written time; the step logs; `postProcessing/`.
A node keeps nothing: what is not returned is gone when the run ends.

## Studies

`OutWit.Controller.Sweep` runs a parameter study over a case in one of two
forms:

- **A case with tokens.** Values in the case's dictionaries are replaced by
  tokens (`{{oc1}}`, ...) in the files marked as templated; every variant is the
  same case with its own values, substituted on the node byte for byte. Before
  any node sees a task, the Sweep host checks that every token has a place in a
  templated file and every place a declared token.
- **A case set.** Every variant is its own ready case - its own tree and recipe,
  nothing templated - and the study carries what the cases share: the ranks, the
  responses, the artifact policy.

---

OpenFOAM® is a registered trademark of OpenCFD Limited. This offering is not
approved or endorsed by OpenCFD Limited, producer and distributor of the
OpenFOAM software via www.openfoam.com, and owner of the OPENFOAM® and
OpenCFD® trade marks. The kit is built from OpenFOAM's sources under the
GPL-3.0; the kit's repository carries the licence and the source offer.
