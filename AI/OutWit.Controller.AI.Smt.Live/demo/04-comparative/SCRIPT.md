# 04 - The comparative test: one machine against the fleet

One problem set, two ways, and the table of the two:

- **One machine** (`smt-live local`): the set solved on that machine alone by the controller's own
  code, the very code a node and the server run, with the pinned solver module and the pinned
  native engine library, at the node's degree rule or at as many sessions as the machine has
  cores. Measured from the tool's start ("files ready") to the four documents on disk.
- **The fleet** (`smt-live run`): the same set and the same options on a server and its nodes,
  measured end to end on the operator's machine: connecting, the upload, the job (the server cuts
  the set into packages, the nodes solve them, copies of the sampled packages on other machines,
  the server settles the run, every `sat` model through its evaluator, and writes the documents)
  and the downloads.
- **The table** (`smt-live comparative`): whether both ran the same thing (the set's SHA-256, the
  module, the options but the sampling rate), the wall times and their parts, the problems a
  second, the verdicts, the failed validations, the retries and the copies that disagreed, every
  row's digest against the other side's (the strongest sign that the two ran the same thing: a
  digest covers the answer, its models and its cost), the flips, and the crossover: the set size
  above which the fleet finishes first, with what it assumes, or that this machine finishes first
  at every size a set may have.

The set is the long-tail kit ([`../03-long-tail`](../03-long-tail/SCRIPT.md): problems of
QF_LIA and QF_BV of known status that took Z3 1 to 60 s), with the options its script gives;
[`../01-known-answers`](../01-known-answers/SCRIPT.md) serves for a rehearsal. The small set that
measures the fleet's fixed cost is [`../02-eight-queens`](../02-eight-queens/SCRIPT.md).

## What is counted, and what is not

| | One machine | The fleet |
|---|---|---|
| Starts | when the tool starts on that machine, the set and the tool already there | when the tool starts on the operator's machine, the set already there |
| Ends | the four documents on that machine's disk | the four documents on the operator's disk |
| Inside | the start-up (the module's compile), reading and cutting the set, solving, settling (the evaluator on every `sat` model), writing | connecting, the upload, the server's queue, the job (cutting, solving with the sampled copies, settling, writing), the downloads |
| Not inside | making the set (the same for both sides); copying the tool and the set to the machine and the documents back (installing a solver is not counted either) | making the set |

Both sides take the same `problems.jsonl` (the same SHA-256) and the same options; `--rate` is the
fleet's alone (one machine makes no copies). The machine of the local side is out of the fleet
while it runs, and runs nothing else of note.

## Steps

From `AI/OutWit.Controller.AI.Smt.Live` on the operator's machine (Git Bash on Windows), with the
environment of the README set for the fleet's runs. `SET` is the set's file, `OPTIONS` its options
from its own script, `RATE` the fleet's sampling rate, `OUT` a folder of the four runs:

```
SET=demo-out/long-tail/problems.jsonl
OPTIONS="<the options of the set's SCRIPT.md, but --rate>"
RATE=<the fleet's sampling rate>
OUT=demo-out/comparative
dotnet build -c Release
```

### 1. The fleet: the small set, then the set

The small run measures the fleet's fixed cost: run it right before the compared run, with the
same options and rate, on the same fleet.

```
bin/Release/net10.0/smt-live run demo/02-eight-queens/problems.jsonl $OPTIONS --rate $RATE --out $OUT/fleet-small
bin/Release/net10.0/smt-live run $SET $OPTIONS --rate $RATE --out $OUT/fleet
```

Each folder gets the four documents and `fleet.json`, what the run measured.

### 2. One machine: a Mac over ssh

The tool for macOS on Apple silicon, self-contained, with the module and the Mac's native library
beside it, checked against the pins and zipped; then one folder on the Mac for the tool, the set
and the run, which the last step removes. `MAC` is the Mac's ssh alias, `DIR` the folder there.
The run goes on without the ssh session (`nohup`); its temporary files stay inside the folder
(`TMPDIR`, and no diagnostics socket of the runtime).

```
bash publish-local.sh osx-arm64 demo-out/smt-live-osx-arm64
MAC=<the Mac's ssh alias>
DIR=/tmp/smt-local
ssh $MAC "mkdir $DIR"
scp demo-out/smt-live-osx-arm64.zip $SET $MAC:$DIR/
ssh $MAC "cd $DIR && unzip -q smt-live-osx-arm64.zip -d smt-live && mkdir tmp && sysctl -n hw.ncpu"
ssh $MAC "cd $DIR && TMPDIR=$DIR/tmp DOTNET_EnableDiagnostics=0 nohup ./smt-live/smt-live local $(basename $SET) $OPTIONS --out local < /dev/null > local.log 2>&1 &"
ssh $MAC "tail -4 $DIR/local.log"
```

`publish-local.sh` prints the SHA-256 of the module, the library and the zip; `local.log` prints
them again as loaded. Should macOS kill the tool at its start ("Killed: 9"), sign it on the Mac
with `ssh $MAC "codesign --force --sign - $DIR/smt-live/smt-live"` (never the library). Ask
`tail` again until the log's last line is `result   : ...`. Then, for as many sessions as the Mac
has cores (the number `sysctl` printed; the node's rule, the default, may give fewer for the
memory), once more into another folder:

```
ssh $MAC "cd $DIR && TMPDIR=$DIR/tmp DOTNET_EnableDiagnostics=0 nohup ./smt-live/smt-live local $(basename $SET) $OPTIONS --degree <cores> --out local-cores < /dev/null > local-cores.log 2>&1 &"
ssh $MAC "tail -4 $DIR/local-cores.log"
```

When both have ended, the documents come back and the folder goes:

```
scp -r $MAC:$DIR/local $OUT/local
scp -r $MAC:$DIR/local-cores $OUT/local-cores
scp $MAC:$DIR/local.log $MAC:$DIR/local-cores.log $OUT/
ssh $MAC "rm -rf $DIR"
ssh $MAC "ls $DIR"
```

The last command must say there is no such folder.

### 3. The table

```
bin/Release/net10.0/smt-live comparative $OUT/local $OUT/fleet --fixed $OUT/fleet-small
bin/Release/net10.0/smt-live comparative $OUT/local-cores $OUT/fleet --fixed $OUT/fleet-small
```

## What to look at

1. **The same thing**: `set`, `module` the same, `options` the same but the sampling rate. If not,
   the rest compares two different runs.
2. **The rows**: `digests: N of N equal`, `flips ... 0`. Equal digests say the two sides solved the
   same problems with the same module under the same options and got the same answers, models and
   costs; a difference is listed by its index and id.
3. **The table**: the wall time of each side and its parts, the problems a second; the verdicts,
   the failed validations (both 0 on an honest run), the retries and the node faults of the fleet,
   the copies that disagreed.
4. **The crossover**: the two lines (each side's fixed cost and cost a problem), the size where
   they cross, and the assumptions printed under it. `measured` gives which side finished first
   at the measured size, whatever the lines say.
5. **Operator effort**, which the tool does not measure: the steps above are, on the fleet's side,
   two runs of the tool with the environment set; on one machine, building the tool for it, two
   copies there, unpacking, a run (or two) followed until its end, copying back and removing the
   folder. Note beside the table anything that took more than that (a retry by hand, a lost ssh
   session, a fleet that had to be woken).

## Measured

The first run of this script fills a table here: the date and the controller's version, the
machines on each side, the set and the options, each side's wall time and its parts, the problems
a second, the rows compared and equal, the flips, the failed validations, the retries, and the
crossover with its lines, at the node's degree and at as many sessions as the Mac has cores.
