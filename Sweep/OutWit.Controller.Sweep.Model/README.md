# OutWit.Controller.Sweep.Model

Shared data types of the Sweep orchestration controller
(`OutWit.Controller.Sweep`) - a parameter study as data, for any solver
family:

- `SweepOptionsData` - the study as the initiator submits it: the parameters
  with their placeholder tokens (`SweepParameterData`), the variant table
  (`SweepVariantData`), the chunk progression bounds, and **exactly one family
  block** with what the variants run on:
  - `CalculiX` (`SweepCalculiXStudyData`) - a template study (one base deck
    with baked tokens) or a deck set (every variant's own deck,
    `SweepCalculiXDeckData`), the mesh size, the thread policy, the extraction
    request;
  - `OpenFOAM` (`FoamCaseData` from `OutWit.Controller.OpenFOAM.Model`) - the
    case every variant runs with its own token values, or, in a case set,
    what every case shares.
- `SweepOpenFOAMSetData` - the cases of an OpenFOAM case set, a job input
  beside the study: every variant's own ready case (`SweepOpenFOAMCaseData`:
  its name, its tree, its recipe, its cell count and solver class).
- `SweepPlanData` - the validated study, its case set when it has one, and
  its chunk schedule, immutable.
- `SweepStateData` - the cursor carried across chunks: the next chunk, the
  counts by outcome, the latest manifest blob and the result index
  (`SweepResultIndexEntryData`: variant, outcome, label, artifacts by kind -
  `SweepArtifactData`, `SweepArtifactKind`).
- `SweepManifestData` - everything harvested so far; each row
  (`SweepManifestRowData`) is the sweep's verdict (`SweepOutcome`: succeeded,
  failed, refused) and the node's own result, verbatim, in its family's
  member (`CcxResultData`, `FoamResultData`).

A new solver family is an addition: a `SweepFamily` member, an options block
and a manifest-row member, each appended.

The state and its index publish the `sweep.*` document vocabulary
(`sweep.state@2`, `sweep.resultIndexEntry@2`, `sweep.artifact@1`) for
initiators that are not .NET: `Documents/` carries the JSON schema and the
generated C++ and Python bindings, regenerated from this assembly (CI checks
them for drift).

All types follow the OutWit model paradigm: `ModelBase` with value-based `Is`
comparison and `Clone`, MemoryPack-serializable with append-only layouts.
