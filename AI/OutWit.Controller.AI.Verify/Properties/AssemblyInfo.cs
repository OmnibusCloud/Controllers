using System.Runtime.CompilerServices;

// The wasm sandbox host (OutWit.Controller.AI.Wasm, compiled in from AI/Shared/Wasm) is internal
// to every controller that carries it; the test project exercises it directly - the arrangement
// the Render, Sweep and ParaView controllers use.
[assembly: InternalsVisibleTo("OutWit.Controller.AI.Verify.Tests")]
