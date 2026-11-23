using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt;

/// <summary>
/// The Smt controller: SMT problems solved by a pinned solver that runs as a WebAssembly module
/// in the controller's own wasm sandbox, under a budget that is the same on every machine. This
/// version holds the solver itself (<see cref="Solver.SmtSolver"/>: one problem, one fresh
/// instance, one outcome); it registers no variable and no activity yet.
/// </summary>
[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
public sealed class WitControllerSmtModule : WitPluginBase, IWitControllerNode, IWitControllerHost
{
    /// <summary>Registers what the controller gives the engine: nothing yet.</summary>
    /// <param name="services">The service collection of the engine host or node.</param>
    public override void Initialize(IServiceCollection services)
    {
    }
}
