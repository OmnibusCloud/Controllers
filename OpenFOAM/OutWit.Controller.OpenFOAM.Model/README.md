# OutWit.Controller.OpenFOAM.Model

Shared data types of the OpenFOAM® case controller (`OutWit.Controller.OpenFOAM`)
and the Sweep orchestration controller (`OutWit.Controller.Sweep`), and the
rules both enforce:

- `FoamCaseData` — a case as a study runs it: the base tree as file
  references (`FoamFileRefData`), the allow-listed recipe (`FoamRecipeData`,
  `FoamStepData`), the rank policy, the response request, the artifact
  policy, and the cell count and solver class for work estimation. The same
  for every variant of a study.
- `FoamTaskData` / `FoamResultData` — one variant's run: the case and the
  variant's token values (`FoamTokenValueData`); back come per-step exit
  codes and times (`FoamStepOutcomeData`), the convergence facts, the
  extracted response row, the artifact reference, or the reasons the case was
  refused before anything ran.
- `FoamExtractionRequestData`, `FoamResponseSpecData`, `FoamResponseRowData` —
  the response set evaluated on the node right after the run.
- `FoamArtifactPolicyData` — what of the finished case travels back.

`Rules/` holds what a case may be, as code every party runs: `FoamAllowList`
(the utilities and the solver shape), `FoamRecipeRules` (steps and argument
grammar; the kit is asked through a predicate), `FoamCasePathRules` (the base
tree: paths inside the case, no spaces, no case-only collisions, no leftovers
of an earlier run), `FoamResponseRules`, `FoamTemplating` (substitution and
token coverage) and `FoamCaseRules`, the one entry that checks a case from its
data before anything is downloaded.

All types follow the OutWit model paradigm: `ModelBase` with value-based `Is`
comparison and `Clone`, MemoryPack-serializable with append-only layouts.

OpenFOAM® is a registered trademark of OpenCFD Limited. This offering is not
approved or endorsed by OpenCFD Limited, producer and distributor of the
OpenFOAM software via www.openfoam.com, and owner of the OPENFOAM® and
OpenCFD® trade marks.
