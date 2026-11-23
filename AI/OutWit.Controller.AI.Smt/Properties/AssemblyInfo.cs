using System.Runtime.CompilerServices;

// The wasm sandbox host (OutWit.Controller.AI.Wasm, compiled in from AI/Shared/Wasm) and the
// solver's working types are internal to the controller; the test project exercises them
// directly - the arrangement Verify and Corpus use for their copies of the host.
[assembly: InternalsVisibleTo("OutWit.Controller.AI.Smt.Tests")]
