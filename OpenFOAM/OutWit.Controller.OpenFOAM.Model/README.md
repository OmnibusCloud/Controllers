# OutWit.Controller.OpenFOAM.Model

Shared data types of the OpenFOAM® case controller (`OutWit.Controller.OpenFOAM`)
and the Sweep orchestration controller (`OutWit.Controller.Sweep`):

- `FoamTaskData` / `FoamResultData` — one variant's run: the base case as file
  references (`FoamFileRefData`), the variant as token substitutions
  (`FoamTokenValueData`), the allow-listed recipe (`FoamRecipeData`,
  `FoamStepData`), the thread policy, the response request and the artifact
  policy; back come per-step exit codes and times (`FoamStepOutcomeData`),
  the convergence facts, the extracted response row, the artifact reference,
  or the reasons the case was refused before anything ran.
- `FoamExtractionRequestData`, `FoamResponseSpecData`, `FoamResponseRowData` —
  the response set evaluated on the node right after the run.
- `FoamArtifactPolicyData` — what of the finished case travels back.

All types follow the OutWit model paradigm: `ModelBase` with value-based `Is`
comparison and `Clone`, MemoryPack-serializable with append-only layouts.

OpenFOAM® is a registered trademark of OpenCFD Limited. This offering is not
approved or endorsed by OpenCFD Limited, producer and distributor of the
OpenFOAM software via www.openfoam.com, and owner of the OPENFOAM® and
OpenCFD® trade marks.
