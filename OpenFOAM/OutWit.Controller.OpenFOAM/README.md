# OutWit.Controller.OpenFOAM

Runs complete [OpenFOAM®](https://www.openfoam.com/) cases on WitCloud
compute nodes. One run = one whole case on one node - meshing, decomposition,
the solver and the post step, through an allow-listed recipe; case support is
whatever the pinned kit solves. The win is throughput across many independent
runs, which is exactly what the companion `OutWit.Controller.Sweep`
orchestrates into parameter studies.

## Activity

| Activity | Side | Purpose |
|---|---|---|
| `Foam.Run(FoamTask) → FoamResult` | node | Materialise the variant's case from the base files and its token substitutions, refuse it by name if it carries run-time code or an unknown step, run the recipe under the bundled kit in a scratch directory (parallel steps under the kit's MPI on the controller-written `decomposeParDict`, scotch), read the convergence facts from the solver log and the requested responses from `postProcessing/`, zip and upload what the artifact policy asks for. A failed step, a diverged solve or a refused case is **data** in the result, not a task failure. |

The task rides as one envelope (`FoamTaskData`): the base case as blob
references (the same blobs for every variant, fetched once per node), the
variant as token values, the recipe, the thread policy, the response request
and the artifact policy; the cell count and solver class ride as scalars so
work estimation never opens a file. Results return in completion order -
consumers map by `VariantIndex`, never positionally.

## What a case may contain

The kit ships no compiler, so anything that compiles C++ at run time is
refused before the first process starts, with file and line: `codeStream`,
`#codeStream`, `#calc` (`#eval` is the in-built evaluator and is allowed),
`coded*` conditions and function objects, a `dynamicCode/` directory. Also
refused: `libs` entries naming libraries outside the kit, `#include` paths
outside the case, a decomposed-only case, a recipe step outside the
allow-list (`FoamAllowList`), an argument the allow-list's grammar does not
accept, a path with a space (OpenFOAM strips whitespace from paths).

## Bundled kit

The module carries pinned **OpenFOAM v2606** kits for `linux-x64` and
`osx-arm64` as controller data assets, produced and mirrored by
[OmnibusCloud/OpenFOAM](https://github.com/OmnibusCloud/OpenFOAM) (the
Windows kit joins with its release). Nodes need no preinstalled software: the
kit bundles Open MPI 4.1.8, scotch and fftw, and is relocatable by
environment - the kit's `KIT.env` records the exact environment its build
established, the controller substitutes the kit folder and the task's scratch
and sets the result on the solver process, with `HOME` and `TMPDIR` inside
the scratch. Nothing is sourced on a node; a run writes into the case
directory and the scratch and nowhere else. On Windows the kit runs on the
node's own MS-MPI where the machine owner has installed it, serially otherwise.
OpenFOAM is GPL-3.0: the kit ships the licence text and the written source
offer, and the corresponding source is publicly mirrored in that repository's
releases.

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

`Variables` (module dependency). The shared data types live in
`OutWit.Controller.OpenFOAM.Model`, consumed by this controller, by the Sweep
orchestration controller, and by client applications reading case-sweep
manifests.
