using System.Runtime.CompilerServices;

// The solver families (ISweepFamily and its implementations) are the host's
// internal seam; the test project exercises them directly - the arrangement
// the Render and ParaView controllers use.
[assembly: InternalsVisibleTo("OutWit.Controller.Sweep.Tests")]
