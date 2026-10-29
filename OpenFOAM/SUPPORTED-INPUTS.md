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

A link inside the case folder - a directory junction, a symbolic link to a
folder or to a file - is the file or folder it points to when its target lies
inside the case (OpenFOAM's tutorials keep `system/<region>/fvSolution ->
../fvSolution`), and is refused by its path, never entered, when it points
outside: a case travels as the files it holds, never what a link points to
(`FoamCaseFolderRules`); copy the files into the case.

Every file travels with its size and SHA-256 as the initiator read it, and the
node verifies the bytes it has against them before the file lands in the case:
a case that changed after it was read refuses the variant, by file
(`FoamCaseBlobRules`).

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
| a function object of a type outside the list below, or one naming a path outside the case - wherever it is written: a block of `functions`, a block of a file it `#include`s, a dictionary of the case's `system/` that an `#includeFunc` pulls in | a pool node is another person's machine; a case may carry only measurements and writers that stay inside it |
| a `functions` entry that is not a block (`functions #includeEtc "...";`, `functions $macro;`) | what it pulls in cannot be read, and the controller cannot add a response to it |

Files larger than 8 MB (meshes, large fields) are not read for these
constructs.

### Function objects

The `functions` dictionary of `controlDict` may carry only these types - the
kit's own measurements, derived fields and writers, each writing under the
case's `postProcessing/` or into a time folder. The kit also carries function
objects that reach the node (`systemCall` runs a command, `timeActivatedFileUpdate`
copies a file of the node into the case, `writeDictionary`, `externalCoupled`,
`abort`); a case carrying one is refused by name and line, and every solver
process runs with `allowSystemOperations` off, so nothing of a case's runs a
command whatever the rules miss. The rule reads every function object the
case runs, the way v2606 finds them: the blocks of the last top-level
`functions`, the blocks of a case file it `#include`s, and an `#includeFunc
<name>` as the case's own `system/<name>` when the case ships one (OpenFOAM
looks there first) - a dictionary of the kit's own
`etc/caseDicts/postProcessing` is the kit's and passes. The same reading
(`FoamCaseFunctionObjects`) tells the node which responses the case already
measures itself and an initiator what a case offers, a function object with
a `coordinateSystem` or an `origin` in place of `CofR` included.

`forces` `forceCoeffs` `bladeForces` `propellerInfo` `phaseForces` `fieldMinMax` `fieldStatistics` `fieldAverage` `fieldExtents` `valueAverage` `volFieldValue` `surfaceFieldValue` `multiFieldValue` `histogram` `momentum` `momentumError` `continuityError` `flux` `fluxSummary` `interfaceHeight` `regionSizeDistribution` `sizeDistribution` `probes` `patchProbes` `thermoCoupleProbes` `radiometerProbes` `sets` `surfaces` `streamLine` `wallBoundedStreamLine` `nearWallFields` `add` `subtract` `multiply` `divide` `pow` `mag` `magSqr` `components` `norm` `randomise` `grad` `div` `ddt` `ddt2` `curl` `vorticity` `enstrophy` `Lambda2` `Q` `LambVector` `flowType` `pressure` `hydrostaticPressure` `CourantNo` `surfaceCourantNumber` `MachNo` `PecletNo` `wallShearStress` `wallHeatFlux` `yPlus` `heatTransferCoeff` `turbulenceFields` `DESModelRegions` `blendingFactor` `stabilityBlendingFactor` `derivedFields` `exprField` `age` `comfort` `columnAverage` `fieldCoordinateSystemTransform` `limitFields` `zeroGradient` `reference` `resolutionIndex` `streamFunction` `surfaceInterpolate` `surfaceDistance` `writeCellCentres` `writeCellVolumes` `binField` `energySpectrum` `proudmanAcousticPower` `Curle` `XiReactionRate` `BilgerMixtureFraction` `electricPotential` `energyTransport` `scalarTransport` `AMIWeights` `processorField` `DMD` `psiReactionThermoMoleFractions` `rhoReactionThermoMoleFractions` `psiSpecieReactionRates` `rhoSpecieReactionRates` `psiReactionsSensitivityAnalysis` `rhoReactionsSensitivityAnalysis` `multiphaseInterHtcModel` `reactingEulerHtcModel` `viewFactorHeatFlux` `cloudInfo` `dataCloud` `particleDistribution` `extractEulerianParticles` `icoUncoupledKinematicCloud` `dsmcFields` `writeObjects` `readFields` `removeRegisteredObject` `ensightWrite` `ensightCloud` `vtkWrite` `vtkCloud` `areaWrite` `runTimeControl` `timeInfo` `solverInfo` `caseInfo` `foamReport` `log` `setTimeStep` `setTimeStepFaRegion` `setFlow` `syncObjects` `agglomerationInfo` `parProfiling` `cellDecomposer` `mapFields` `multiRegion` `relativeVelocity` `graphFunctionObject`

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
  copy as it was, rather than rewrite it. The controller adds steps of this
  kind on its own, right before the first solve, for every patch value, volume
  value, min/max and probe of the task that neither the recipe includes nor
  the case runs itself (see Responses); they appear in the result's steps
  like any other.

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
| `ForceCoeffs` | `forceCoeffs` | at least one patch; the centre (`CofR`, `origin` or a `coordinateSystem`); `magUInf`, `lRef`, `Aref` and `rhoInf` whatever `rho` says; with `CofR`, `liftDir` and `dragDir` too |
| `Forces` | `forces` | at least one patch; the centre (`CofR`, `origin` or a `coordinateSystem`); `rhoInf` when `rho rhoInf` |
| `PatchValue` | `surfaceFieldValue` | exactly one patch, at least one field, an operation (`areaAverage`, `areaIntegrate`, `min`, `max`, ...) |
| `VolumeValue` | `volFieldValue` | at least one field, an operation (`volAverage`, `volIntegrate`, `min`, `max`, ...) |
| `FieldMinMax` | `fieldMinMax` | at least one field |
| `Probe` | `probes` | at least one field, a `probeLocations` parameter |

A response's name is a word, unique in the request, and not the name of a file
the case carries in `system/`. A patch is a word or a quoted regular
expression; a field is a word that may carry dots, as phases are named
(`alpha.water`) - never a colon, since a probe writes one file per field. A
force the case does not write itself is measured as the recipe says: during
the solve (`includeFunc`, which a force on a turning wall needs) or after it
by a post step. A patch value, a volume value, a min/max and a probe are
measured by the solver at every step whatever step the recipe names for them
- the controller adds them to the solve itself, right before it, with a
`log.includeFunc` of its own - and a post step the recipe names for one of
them is recorded but not run. Why: a post step (`postProcess -func`,
`-latestTime`) reads the latest written time, which is not the final time
when `endTime` is not a multiple of `writeInterval` (sweeping either does
that), so the response came back blank; and for a probe the post step
overwrites the solve's rows with that one time (both measured on v2606,
2026-10-28). A value is reported from the run's final time, whichever step
wrote it: a function object that stopped writing earlier - the case's own,
writing at write times only, included - is left out and named in
`log.responses`. A probe column is named `<field>@probe<N>(x y z)` from the
probe's header line, with `_x`, `_y`, `_z` for a vector (`_xx` ... `_zz` for a
tensor); a probe outside the mesh is an empty cell, named in `log.responses`
with its position, never the -1e+300 OpenFOAM writes for it. In a parameter study a response parameter may carry the
study's token (a reference speed that follows the swept speed). A force
response that re-measures one of the case's own force function objects -
named as it, or as it with `_sweep` - follows the case where the study
sweeps it: every reference value whose entry carries a token (a `$`
reference to such an entry included) takes the variant's value before the
node writes the response, and `log.responses` says which followed and to
what; a value the study does not sweep stays as requested. A parameter's
value is plain dictionary text - a word, a number, a vector, a list - or a flat
sub-dictionary of such entries (`coordinateSystem { origin (0 0 0); e1 (1 0 0);
e3 (0 0 1); }`, a `binData`); never braces inside braces, never code. A force
function object without the entries it reads stops the solve ("Entry 'rhoInf'
not found" - measured on v2606, 2026-10-28), so a request missing one is
refused by name before it runs.

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
With the logs comes `log.responses`, what the run says beyond the numbers: a
response left out and why (a stale row, a probe outside the mesh), a reference
value a re-measured force took from the case, and the advice a task gives -
said, not refused - when it would run without doing what was meant: ranks
asked for while no step of the recipe runs in parallel (every step then runs
serially on one rank), or a `setFields` step that overwrites an `internalField`
the study sweeps (everywhere through `defaultFieldValues`, or inside the
regions only). An initiator shows the same sentences at preflight
(`FoamCaseAdvice`). A node keeps nothing: what is not returned is gone when
the run ends.

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
