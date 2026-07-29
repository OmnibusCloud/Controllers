# OutWit.Controller.CalculiX.Model

Shared data types of the CalculiX solve controller (`OutWit.Controller.CalculiX`)
and the Sweep orchestration controller (`OutWit.Controller.Sweep`):

- `CcxTaskData` / `CcxResultData` — one variant's solve: deck blob reference,
  explicit node/element counts (work estimation without opening the blob),
  thread policy, extraction request; back come artifact blob ids, exit code,
  measured solve time and the extracted response row.
- `CcxExtractionRequestData`, `CcxProbeData`, `CcxResponseRowData` — the
  response set extracted on the node right after the solve.
- `SweepOptionsData`, `SweepPlanData`, `SweepStateData`, `SweepManifestData` —
  a parameter study as data: parameters with placeholder tokens, the variant
  table, progressive chunk sizes, the cursor state carried across chunks, and
  the manifest of everything harvested so far.

All types follow the OutWit model paradigm: `ModelBase` with value-based `Is`
comparison and `Clone`, MemoryPack-serializable with append-only layouts.
