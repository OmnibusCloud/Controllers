# OutWit.Controller.CalculiX

Runs complete [CalculiX](http://www.calculix.de/) (ccx) decks on WitCloud
compute nodes. One solve = one whole deck on one node — no decomposition, no
model conversion; deck support is whatever the pinned ccx solves. The win is
throughput across many independent solves, which is exactly what the
companion `OutWit.Controller.Sweep` orchestrates into parameter studies.

## Activity

| Activity | Side | Purpose |
|---|---|---|
| `Ccx.Solve(CcxTask) → CcxResult` | node | Download the variant deck, run the bundled ccx (`<ccx> <jobname>` in a scratch directory - a scope of the temp folder the host hands the controller, on a node the client's controllers' temp folder - `OMP_NUM_THREADS` from the task), upload `.frd`/`.dat` as blobs, extract the requested responses on the node, return artifact ids + exit code + measured solve time + the response row. A nonzero solver exit is **data** in the result, not a task failure. |

The task rides as one envelope (`CcxTaskData`): deck blob id, explicit
node/element counts (work estimation never opens the blob), thread policy and
the extraction request. Results return in completion order — consumers map by
`VariantIndex`, never positionally.

## Responses

The node reads the variant's `.frd` and `.dat` right after ccx exits and
returns numbers, not files: the response row (`CcxResultData.ResponseRow`, a
list of named values) and the notes, carried as note lines inside
`CcxResultData.LogTail` (see *Notes* below). A column appears only when the
deck asked ccx for the output it is computed from; the controller adds no
output requests to a deck.

### Columns

| Column | From | What it is |
|---|---|---|
| `max_disp_magnitude`, `max_disp_x`, `max_disp_y`, `max_disp_z` | `.frd` DISP (`*NODE FILE` with `U`) | Largest displacement magnitude and largest absolute component per axis over the nodes of the step's last DISP block |
| `max_von_mises`, `min_von_mises` | `.frd` STRESS (`*EL FILE` with `S`) | Largest and smallest nodal von Mises stress of the step's last STRESS block |
| `max_temp`, `min_temp` | `.frd` NDTEMP (`*NODE FILE` with `NT`) | Largest and smallest temperature of the step's last NDTEMP block |
| `max_temp_any_step` | `.frd` NDTEMP | Peak temperature over every NDTEMP block of the step; only when the step wrote more than one (a transient) |
| `eigenfrequency_<k>` | `.dat` eigenvalue table | Frequency in cycles per time of mode `k`, numbered as ccx numbers the modes of that table |
| `eigenfrequency_nd<n>_<k>` | `.dat` eigenvalue table of a cyclic-symmetry solve | Mode `k` of nodal diameter `n` |
| `buckling_factor_<k>` | `.dat` buckling table | Buckling factor `k` |
| `rf_sum_x@<set>`, `rf_sum_y@<set>`, `rf_sum_z@<set>` | `.dat` forces block (`*NODE PRINT, NSET=<set>` with `RF`) | Sum of the reaction forces of the set in the global frame, from the last block of the step; the set name is lowercased |
| `<max\|min\|avg>_<von_mises\|disp_magnitude\|temp>@<set>` | `.frd`, a probe of the request | The aggregate over the nodes of the deck set, from the step's last block of that field; the set name is lowercased |

**Steps.** Every column belongs to a step. A single-step deck keeps the plain
names above. A multi-step deck (more than one `*STEP`, counted in the deck or
seen in the files) names the step in every column: `max_von_mises[step 1]`,
`eigenfrequency_1[step 2]`, `rf_sum_z@fix[step 1]`, `max_temp@tip[step 3]`. The
values of a step come from that step's last block of each field, so a static
step followed by a frequency step reports the static stress under `[step 1]`
and the mode-shape stress under `[step 2]`; before (controller 1.1.3 and
earlier) the last block of the whole file won the plain name, a static 362
became 606 266, and a static reaction of 900 became 0. Eigenfrequencies and
buckling factors are numbered within their step, as ccx numbers them: a 5-mode
step followed by a 3-mode step gives `eigenfrequency_1..5[step 1]` and
`eigenfrequency_1..3[step 3]`, never 6..8 (a variant where ARPACK returned one
mode fewer shifted every later column before).

**Eigenvalue tables.** ccx prints the table in four layouts and the frequency
in cycles is read from the right column of each: plain (`writeev.f`), cyclic
symmetry with a nodal-diameter column (`writeevcs.f`, one table per nodal
diameter, hence `eigenfrequency_nd<n>_<k>`), complex without an eigenvalue
column (`writeevcomplex.f`, `*COMPLEX FREQUENCY`), and both. A table in any
other layout yields no column and a warning note. A negative eigenvalue (a
rigid-body mode) prints as frequency 0 in ccx's own table and is reported so.

**Reaction forces.** The global total ccx prints for `*NODE PRINT, TOTALS=YES`
or `TOTALS=ONLY` is taken as printed (it is summed from the global components,
and it is the only sum a `TOTALS=ONLY` print offers). Otherwise the rows are
summed, and every row ccx printed in a local frame (the trailing `L`;
`*NODE PRINT` prints in the node's `*TRANSFORM` frame by default) is rotated
back to the global frame through the deck's own `*TRANSFORM` - rectangular or
cylindrical, built at the node as ccx builds it - with an info note. A set
whose local rows cannot be rotated (no deck, no `*TRANSFORM` covering the
node, no coordinates) gets no `rf_sum` columns and a warning note telling the
user to print with `GLOBAL=YES` or `TOTALS=YES`. The forces ccx prints per
mode after an eigenvalue solve (`*FREQUENCY`, `*BUCKLE`) are mode shapes and
are never summed; a `*BUCKLE` step reports the base state's reaction printed
before the factors, with an info note about the mode prints.

**Probes on expanded elements.** With the default `OUTPUT=3D`, the `.frd` of a
shell, beam, truss, membrane or plane-element deck holds the expanded 3D
mesh only, under new node numbers (an S8R deck of nodes 1-289 came back as
nodes 370-1236). A deck node of the probe set that the file does not list is
mapped by position: to the result nodes at its own position (the mid-surface
node of a shell corner node) when there are any, otherwise to its nearest
result nodes, all of them when they tie (the top and bottom nodes of a
quadratic shell's mid-side node, the section nodes of a beam node), within a
tolerance of 1e-5 of the largest coordinate magnitude (the six digits the
`.frd` stores). `max` and `min` run over every result node standing for the
set; `avg` weights each deck node once. An info note on the column counts the
nodes taken by nearest position; a deck node with no result node at all is
left out, with an info note, and a set with none gets no column and a warning.

### Columns that are not reported

A column that would carry a wrong number is left out, with a warning note on
the column (the note line's column field names it) saying why:

- `max_von_mises`, `min_von_mises` and every `von_mises` probe when the deck
  has shell or beam elements and its file cards say `OUTPUT=2D`: ccx then maps
  the stress onto the mid-surface or the beam axis, where the bending stress is
  gone (an S8R cantilever read 0.81 against 75 analytic, a B32 4.75 against
  59.7). Remove `OUTPUT=2D` to get the expanded-mesh stress.
- Every column of a field whose `.frd` block carries a value that is not a
  number (`nan`, `-nan(ind)`, `inf` - a solve that blew up): the note names the
  field and the node. Before, `nan` parsed to NaN and `-nan(ind)` to 0.
- `rf_sum_*@<set>` when local rows cannot be rotated (above).
- Eigenfrequencies of a table in an unknown layout (above).

### Notes

The notes travel inside `CcxResultData.LogTail`, whose wire layout is the
shipped one (every installed client reads the Sweep manifest in default-mode
MemoryPack, which checks the member count both ways, so no member is added).
`LogTail` is therefore now populated on **successful** variants too, whenever
the node has something to say; it stays `null` for a successful variant
without notes. The grammar (`CcxNoteLog` composes and parses it):

```
<solver tail, as before: the last lines of ccx output, failed solves only>
--- notes ---
[note]<TAB><KIND><TAB><column or empty><TAB><text>
[note]<TAB><KIND><TAB><column or empty><TAB><text>
```

- Lines are separated by `\n`. The solver tail comes first and is absent on a
  successful solve, so such a tail starts with the separator line
  `--- notes ---`. A tail without the separator carries no notes (a failed
  solve with nothing to remark on).
- Every line after the separator that starts with `[note]` is one note:
  `Split('\t', 4)` gives `[note]`, the kind, the column and the text. The
  column and the text never contain a tab or a line break (replaced by
  spaces); the column is empty for a note about the variant as a whole.
- `KIND` is `INFO`, `WARNING` or `ERROR`:

| Kind | Meaning |
|---|---|
| `INFO` | The value stands; the note says how it was obtained (reaction forces rotated from a local frame, probe nodes taken by nearest position, a mode-shape column of an eigenvalue step: `max_disp_*` and `*_von_mises` of a `*FREQUENCY` or `*BUCKLE` step describe the last mode shape, normalised). |
| `WARNING` | A column is not reported (the column field names it; the text says why), or the deck was changed before the solve: a step card naming a solver the bundled ccx does not link (`SOLVER=PASTIX` anywhere, `SOLVER=PARDISO` on a macOS node, `TAUCS`, `SGI`) has the parameter removed and ccx takes its default - PARDISO on Windows and Linux, SPOOLES on macOS; such a card ended the solve with exit 201 before. |
| `ERROR` | The extraction failed as a whole (a reader threw: a file that could not be read, memory that ran out). `ResponseRow` is then **null** with `ExitCode` 0 - that pair is the "extraction failed" signal - and the variant is not a result; before, an empty row passed for a solve with nothing to report. |

The column field carries the final column name (with the `[step N]` suffix in
a multi-step deck); in a multi-step deck a step-scoped note without a column
starts its text with `step N:`. Example of a successful variant's tail:

```
--- notes ---
[note]	WARNING		SOLVER=PASTIX is not linked in the bundled ccx of this node: the parameter was removed from *STATIC and the solve used PARDISO
[note]	INFO	rf_sum_z@fix	9 of 9 rows were printed in the local frame of *TRANSFORM, NSET=FIX and rotated to the global frame
```

### Memory

The `.frd` is streamed through `IFrdResultVisitor`: the reader keeps no
block, the accumulator keeps running extremes per step and field and the
samples of the probed nodes only. A 312 MB `.frd` (300 000 nodes, 40
temperature blocks) took 1 655 MB peak working set and 8.2 s when every block
was held (controller 1.1.3; the probe harness, 2026-10-28); streamed, 45 MB
and 1.7 s.

## Bundled solver

The module carries pinned **ccx 2.22** builds for `win-x64`, `linux-x64` and
`osx-arm64` as controller data assets, produced and mirrored by
[OmnibusCloud/CalculiX](https://github.com/OmnibusCloud/CalculiX). Nodes need
no preinstalled software - the macOS kit carries the GCC runtime
(libgfortran/libgomp/libquadmath/libgcc_s) beside `ccx`, referenced through
`@loader_path`; `ccx-v2.22-1` had linked them by absolute Homebrew paths and
every variant on the first Apple Silicon node died with "dyld: Library not
loaded" (exit 134) - `ccx-v2.22-3`, controller 0.1.8. The Windows kit is built
on MSYS2 winpthreads 13.0.0.r488: the 14.x runtime turns every uncontended
mutex into a kernel wait, and libgfortran takes one per Fortran I/O statement,
so `ccx-v2.22-3` ran the reference cube 1.8x slower than upstream's own Windows
build and Windows workers were rated below Linux ones by the same factor -
`ccx-v2.22-4`, controller 1.0.1, results byte-identical. CalculiX is GPL-2.0:
the asset kit ships the license text and the written source offer, and the
corresponding source is publicly mirrored in that repository's releases.

Node benchmark: `Ccx.Solve` is ranked by the reference deck (a 40³-node static
cube, 64 000 nodes, generated by `CcxReferenceDeck` so it is the same on every
machine) in unit `ccx-static@ref40-v1`. `ref20-v2` (controller 1.0.3 and
earlier) used a 20³ cube that solves in under a second, where process start
and one core decide the time: a 2-vCPU virtual machine tied a 16-core desktop
and the fleet's strongest machines ranked last. The 40-cube takes 8-25 s and
lets the cores count (a Ryzen 9 5950X 8.4 s, a 6-core i7-8700K 10.9 s, a
2-vCPU VM 17.8 s), so a benchmark costs a node half a minute to a minute once
per controller install. Solves run with at most 16 OpenMP threads
(`CcxProcessRunner.MAX_DEFAULT_THREADS`): PARDISO saturates by then and SMT
siblings only cost time. The first solve is
an untimed warm-up - it is the first start of the freshly installed kit, and
its cold page cache and antivirus scan once rated a Ryzen 9 5950X at 1.36 s
against the 0.75 s it takes from then on; the rate is 1 / the median of three
to five timed solves (1.5 s target), so one stall does not move it and a busy
machine is still rated as busy. `ref20-v1` (controller 1.0.1 and earlier)
timed one cold solve. The Custom bag carries the run times (`runs_s`,
`median_s`, `warmup_s`) and the solved maximum displacement (`checksum`).

ccx is started without a console window (`CreateNoWindow`). The worker client
is a windowed application with no console of its own, so before controller
1.0.3 every `ccx.exe` it launched got a fresh console, which cost about 0.45 s
per start on Windows 11: the reference cube took 1.13-1.23 s from the client
against 0.69-0.76 s with the flag, and every sweep variant on a Windows node
paid it.

Determinism note: ccx with OpenMP is not bitwise-reproducible across thread
counts, and the three platform builds add last-digit variation — results are
stable to engineering tolerance, and the controller's tests assert tolerance,
not bits.

SPOOLES runs with one equation-solver thread (`CCX_NPROC_EQUATION_SOLVER=1`,
`CcxEquationSolver`). The kits link SPOOLES multithreaded, and multithreaded
SPOOLES returns a wrong field now and then with exit 0: a 14 k-node bracket
solved 60 times in concurrent pairs came back different in 2 runs (by up to
0.6 %), and a sweep variant on an arm64 Mac was 10 % off. PARDISO and
single-threaded SPOOLES gave the same answer every time. A solve goes through
SPOOLES when a step card says `SOLVER=SPOOLES`, and always on macOS, whose kit
has no PARDISO (ccx's default where it is linked); assembly and stress recovery
keep their OpenMP threads, and the single-threaded factorization cost about
15 % on that bracket. The benchmark's reference solve follows the same rule.

## Dependencies

`Variables` (module dependency). The shared data types live in
`OutWit.Controller.CalculiX.Model`, consumed by this controller, by the Sweep
orchestration controller (a CalculiX study carries the extraction request;
its manifest rows carry the `CcxResultData` verbatim) and by client
applications. The sweep's own types live in `OutWit.Controller.Sweep.Model`.
