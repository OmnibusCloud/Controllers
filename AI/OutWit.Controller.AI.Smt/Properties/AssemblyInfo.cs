using System.Runtime.CompilerServices;

// The wasm sandbox host (OutWit.Controller.AI.Wasm, compiled in from AI/Shared/Wasm) and the
// solver's working types are internal to the controller; the test project exercises them
// directly - the arrangement Verify and Corpus use for their copies of the host.
[assembly: InternalsVisibleTo("OutWit.Controller.AI.Smt.Tests")]

// smt-live (AI/OutWit.Controller.AI.Smt.Live), the controller's developer tool: its local run
// reads, cuts, solves and settles a set by the controller's own code (the reader, the planner, the
// sandbox and the sessions, the settlement, the documents), which is internal. The tool is never
// packed or published.
[assembly: InternalsVisibleTo("smt-live")]
