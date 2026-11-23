# OutWit.Controller.AI.Smt.Scripts

Content-only NuGet package that ships the bundled `.wit` job scripts of the
`OutWit.Controller.AI.Smt` controller. There is no managed assembly in this package: the script
texts and a consumer-side MSBuild `.targets` file that stages them into `$(OutputPath)@Scripts/`,
the layout the runtime reads at start-up.

**Status: 0.1.0, in development; nothing is published.**

## Contents

| Script | Job signature | Gives |
|---|---|---|
| `SmtRun.wit` | `SmtRun(Blob:problems, SmtOptions:opts)` | the blobs `results`, `report`, `provenance`, `integrity` (and the run's `ledger`) |
| `SmtPreflight.wit` | `SmtPreflight(Blob:problems, SmtOptions:opts)` | `result`: an `SmtPreflight` |
| `SmtRuntimeDiagnostics.wit` | `SmtRuntimeDiagnostics()` | `result`: an `SmtRuntimeDiagnostics`; `capabilities`: a blob |

## What SmtRun does

1. **Split.** `Smt.Split(problems, opts)` reads the problem set a row at a time, keeps the refused
   rows for the documents, and cuts the taken problems into packages of problems by the planner's
   rule; the packages travel as blobs.
2. **Solve, sampled.** `Smt.SamplingRate(opts)` gives the share of the packages solved on two
   machines; `Grid.Replicas(2, rate)` and `Grid.ForEach` send each package once, a share of them
   twice, to different machines, and `Smt.SolvePackage` solves them.
3. **Repair.** `Smt.Repairs` sends once more, under the same sampling, the problems that came back
   without an answer the server takes.
4. **Dispute.** `Smt.Disputes` sends to three more machines (`Grid.Replicas(3)`) the problems whose
   copies' digests differ and those whose `sat` model the server's evaluator calls failed.
5. **Settle.** `Smt.Settle` rules on every problem by the digests of its copies, checks every
   `sat` model, and writes the documents into one ledger blob.
6. **Documents.** `Smt.CollectResults`, `Smt.Collect`, `Smt.Provenance` and `Smt.Integrity` hand
   the results, the report, the provenance and the integrity document out of the ledger.

The work is split once and sent once; the repair round and the dispute round are the only second
sends. What the documents say and what a lab can rely on is the controller's
[`docs/capabilities.md`](../OutWit.Controller.AI.Smt/docs/capabilities.md).

## Compatibility

The scripts call the activities the controller has since 0.3.0. The package's compatibility
marker (a reference to the controller with a half-open range, as the repository's decision record
on scripts and controller versions has it) is written in the project and commented out while no
controller is on nuget.org; it waits for the first public release, `[1.0.0, 2.0.0)`, and a test
of the controller's tests holds the range to that release.
