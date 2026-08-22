# OutWit.Controller.CalculiX

Runs complete [CalculiX](http://www.calculix.de/) (ccx) decks on WitCloud
compute nodes. One solve = one whole deck on one node — no decomposition, no
model conversion; deck support is whatever the pinned ccx solves. The win is
throughput across many independent solves, which is exactly what the
companion `OutWit.Controller.Sweep` orchestrates into parameter studies.

## Activity

| Activity | Side | Purpose |
|---|---|---|
| `Ccx.Solve(CcxTask) → CcxResult` | node | Download the variant deck, run the bundled ccx (`<ccx> <jobname>` in a scratch directory, `OMP_NUM_THREADS` from the task), upload `.frd`/`.dat` as blobs, extract the requested responses on the node, return artifact ids + exit code + measured solve time + the response row. A nonzero solver exit is **data** in the result, not a task failure. |

The task rides as one envelope (`CcxTaskData`): deck blob id, explicit
node/element counts (work estimation never opens the blob), thread policy and
the extraction request. Results return in completion order — consumers map by
`VariantIndex`, never positionally.

## Bundled solver

The module carries pinned **ccx 2.22** builds for `win-x64`, `linux-x64` and
`osx-arm64` as controller data assets, produced and mirrored by
[OmnibusCloud/CalculiX](https://github.com/OmnibusCloud/CalculiX). Nodes need
no preinstalled software - the macOS kit carries the GCC runtime
(libgfortran/libgomp/libquadmath/libgcc_s) beside `ccx`, referenced through
`@loader_path`; `ccx-v2.22-1` had linked them by absolute Homebrew paths and
every variant on the first Apple Silicon node died with "dyld: Library not
loaded" (exit 134) - `ccx-v2.22-3`, controller 0.1.8. CalculiX is GPL-2.0: the
asset kit ships the license text and the written source offer, and the
corresponding source is publicly mirrored in that repository's releases.

Determinism note: ccx with OpenMP is not bitwise-reproducible across thread
counts, and the three platform builds add last-digit variation — results are
stable to engineering tolerance, and the controller's tests assert tolerance,
not bits.

## Dependencies

`Variables` (module dependency). The shared data types live in
`OutWit.Controller.CalculiX.Model`, consumed by this controller, by the Sweep
orchestration controller, and by client applications reading sweep manifests.
