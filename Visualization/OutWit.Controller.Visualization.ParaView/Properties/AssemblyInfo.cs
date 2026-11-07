using System.Runtime.CompilerServices;

// The internal seams the test project exercises directly (audit wave 2): ParaViewResultOrdering
// (the frame-set completeness check) and ProcessTreeGuard (the kill-on-close job object) — the
// same arrangement the Render family uses.
[assembly: InternalsVisibleTo("OutWit.Controller.Visualization.ParaView.Tests")]
