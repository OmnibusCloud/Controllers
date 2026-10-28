# OutWit.Controller.CalculiX.Model

Shared data types of the CalculiX solve controller (`OutWit.Controller.CalculiX`):

- `CcxTaskData` / `CcxResultData` — one variant's solve: deck blob reference,
  explicit node/element counts (work estimation without opening the blob),
  thread policy, extraction request; back come artifact blob ids, exit code,
  measured solve time and the extracted response row.
- `CcxExtractionRequestData`, `CcxProbeData`, `CcxResponseRowData` — the
  response set extracted on the node right after the solve.
- `CcxNoteData` — a remark the node attaches to a variant (`CcxResultData.Notes`):
  what it changed before the solve, what it refused or could not read and why,
  how a value was obtained. A note of kind `Error` means the response row is not
  a result. The controller's README lists the notes and the columns they concern.

The Sweep orchestration controller carries these types inside its own
(`OutWit.Controller.Sweep.Model`): a CalculiX study's extraction request and
the node's result in every manifest row. The dependency runs that way only -
this package knows nothing of sweeps.

All types follow the OutWit model paradigm: `ModelBase` with value-based `Is`
comparison and `Clone`, MemoryPack-serializable with append-only layouts.
