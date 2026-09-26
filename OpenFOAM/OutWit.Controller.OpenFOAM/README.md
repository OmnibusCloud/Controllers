# OutWit.Controller.OpenFOAM

Runs complete [OpenFOAM®](https://www.openfoam.com/) cases on WitCloud
compute nodes. One run = one whole case on one node - meshing, decomposition,
the solver and the post step, through an allow-listed recipe; case support is
whatever the pinned kit solves. The win is throughput across many independent
runs, which is what the companion `OutWit.Controller.Sweep` orchestrates into
parameter studies (its `SweepOpenFOAM.wit` fans a case study out to
`Foam.Run`); any script can also drive `Foam.Run` directly through
`Grid.ForEach`.

## Activity

| Activity | Side | Purpose |
|---|---|---|
| `Foam.Run(FoamTask) → FoamResult` | node | Materialise the variant's case from the base files and its token substitutions, refuse it by name if it carries run-time code or an unknown step, run the recipe under the bundled kit in a scratch directory (parallel steps under the kit's MPI on the controller-written `decomposeParDict`, scotch), read the convergence facts from the solver log and the requested responses from `postProcessing/`, zip and upload what the artifact policy asks for. A failed step, a diverged solve or a refused case is **data** in the result, not a task failure. |

A task (`FoamTaskData`) is a case and a variant: the case (`FoamCaseData`,
the same for every variant of a study) carries the base tree as blob
references (fetched once per node), the recipe, the rank policy, the response
request, the artifact policy, and the cell count and solver class as scalars
so work estimation never opens a file; the variant adds only its token values.
Results return in completion order - consumers map by `VariantIndex`, never
positionally.

The rules a case obeys - the allow-list, the recipe grammar, the case paths,
the response request, the token coverage - live in
`OutWit.Controller.OpenFOAM.Model` (`Rules/`), so the node, the Sweep host
and an initiator's preflight refuse the same things with the same sentences.
What needs the kit or the materialised files (executables, libraries,
run-time code) is checked here, on the node.

## What a case may contain

The kit ships no compiler, so anything that compiles C++ at run time is
refused before the first process starts, with file and line: `codeStream`,
`#codeStream`, `#calc` (`#eval` is the in-built evaluator and is allowed),
`coded*` conditions and function objects, a `dynamicCode/` directory. Also
refused: `libs` entries naming libraries outside the kit, include directives
(`#include`, `#sinclude`, `#includeIfPresent`, `#includeEtc`, `#includeFunc`)
whose target leaves the case, a decomposed-only case, a recipe step outside
the allow-list (`FoamAllowList` in the Model), an argument the allow-list's grammar does
not accept, a path with a space (OpenFOAM strips whitespace from paths), and
leftovers of an earlier run in the base case (`log.*`, `postProcessing/`,
`processor*/`), which would pass for this run's. A response name that
collides with a file the case ships under `system/` is refused rather than
overwritten.

## The controller's own step

One step of a recipe is the controller's rather than the kit's:
`restore0Dir -processor`, named after OpenFOAM's `RunFunctions`. A case meshed
on its decomposed form - `decomposePar` over the background mesh, then
`snappyHexMesh` in parallel, as the motorBike tutorial does - needs its
initial fields put into every processor directory afterwards: the fields
`decomposePar` split were cut for a mesh that no longer exists. The step
replaces each `processor*/0` with the initial fields: `0.orig/` when the case
carries one, as in OpenFOAM, otherwise `0/` as it was before the first step
(kept in `0.orig/` for the purpose). It needs a `decomposePar` before it and
never runs under MPI; on a node without MPI the run is serial, there are no
processor directories, and the step logs that it has nothing to do. Its log
is `log.restore0Dir`, like any step's.

## Bundled kit

The module carries pinned **OpenFOAM v2606** kits for `win-x64`, `linux-x64`
and `osx-arm64` as controller data assets, produced and mirrored by
[OmnibusCloud/OpenFOAM](https://github.com/OmnibusCloud/OpenFOAM). Nodes need
no preinstalled OpenFOAM: the Linux and macOS kits bundle Open MPI 4.1.8,
scotch and fftw, and every kit is relocatable by environment - the kit's
`KIT.env` records the exact environment its build established, the controller
substitutes the kit folder and the task's scratch and sets the result on the
solver process, with `HOME` and `TMPDIR` inside the scratch. The scratch is
a scope of the temp folder the host hands the controller (on a node, the
client's controllers' temp folder, Settings > Storage), never a folder of the
controller's own, and it goes back to that folder however the run ends. On
Windows the scratch must leave room for the case's own paths below it: a
temp folder so deep that a run's folder there passes 139 characters (259
minus 120 kept for the deepest paths a decomposed case writes) is refused
with the reason, and the benchmark fails the same way, so the node leaves the
OpenFOAM pool. Nothing is sourced on a node; a run writes into the case
directory and the scratch and nowhere else (the kit folder itself changes only once, when the kit is first
resolved on a node: the Unix executable bits a zip does not keep are
restored, and on Windows the Pstream swap below is made). The Windows kit is
cross-compiled from the same pinned source with MinGW-w64 and ships two
Pstream libraries: the serial one is in place as shipped, and when the node
has Microsoft MPI installed (MS-MPI is the machine owner's to install; its
licence allows redistributing only its installer) the controller copies the
MS-MPI one over it on the first resolution of the kit (idempotent) and runs
parallel steps under the node's `mpiexec`; a node without MS-MPI runs every
step serially. Before a kit is used the controller spot-checks it against its
own `BUILDINFO.txt` (a sample of the listed hashes) and refuses a kit that is
short of a file or carries an altered one, naming the file (a file another
process, such as a scanner, holds for a moment is read again first). It also
refuses, with the reason in words, a kit that is incomplete (no usable
`KIT.env`, no solver) and a kit it cannot run from where it is installed:
under a folder with a space on Linux or macOS (OpenFOAM rejects whitespace in
a path and dies on its own executable path), or on Windows so deep that a kit
file would pass 259 characters (the Windows binaries are not long-path
aware). A node with such a kit fails the `Foam.Run` benchmark with the reason
and so leaves the OpenFOAM pool, instead of failing every variant; only a node
with no kit folder at all (an unsupported platform) keeps the default score.
OpenFOAM is
GPL-3.0: the kit ships the licence text and the written source offer, and the
corresponding source is publicly mirrored in that repository's releases.

Node benchmark: `Foam.Run` is ranked by pitzDaily from the kit's own
tutorials, meshed once and then solved serially for a fixed 50 iterations, in
unit `foam-simple@pitzDaily50-v1`: one untimed warm-up, the rate from the
median of three to five timed runs. Parallel steps run with at most 16 ranks
(`FoamDecomposition.MAX_DEFAULT_RANKS`) when the task asks for all cores.

OpenFOAM® is a registered trademark of OpenCFD Limited. This offering is not
approved or endorsed by OpenCFD Limited, producer and distributor of the
OpenFOAM software via www.openfoam.com, and owner of the OPENFOAM® and
OpenCFD® trade marks.

## Dependencies

`Variables` (module dependency). The shared data types and the case rules
live in `OutWit.Controller.OpenFOAM.Model`, consumed by this controller, by
the Sweep orchestration controller (an OpenFOAM study carries a
`FoamCaseData`; its manifest rows carry the `FoamResultData` verbatim) and by
client applications composing case sweeps.
