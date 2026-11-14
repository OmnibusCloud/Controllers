using System.Runtime.CompilerServices;

// The wasm sandbox host (OutWit.Controller.AI.Wasm, compiled in from AI/Shared/Wasm) is internal
// to every controller that carries it; the test project exercises Corpus's own copy directly - the
// arrangement Verify uses for its copy.
[assembly: InternalsVisibleTo("OutWit.Controller.AI.Corpus.Tests")]
