using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Adapters;
using OutWit.Controller.AI.Smt.Variables;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt;

/// <summary>
/// The Smt controller: SMT problems solved by a pinned solver that runs as a WebAssembly module
/// in the controller's own wasm sandbox, under a budget that is the same on every machine. On a
/// node it solves packages of problems (<c>Smt.SolvePackage</c>: a reference to a blob of problems
/// in, a reference to a blob of rows out) on the node's one budget of sessions, which the problems
/// of all its packages share, and measures the node on a
/// reference set for the allocator; on a host it runs a problem set end to end (<c>SmtRun</c>: the
/// set cut into packages, copies on different machines compared by digest, a repair round, a
/// dispute round, every <c>sat</c> model checked by the host's evaluator, and the documents), says
/// what a set and its options come to before a run (<c>Smt.Preflight</c>), and reports what it
/// carries (<c>Smt.RuntimeDiagnostics</c>, <c>Smt.Capabilities</c>).
/// </summary>
[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
public sealed class WitControllerSmtModule : WitPluginBase, IWitControllerNode, IWitControllerHost
{
    #region WitPluginBase

    /// <summary>Registers the Smt variables, collections and activity adapters.</summary>
    /// <param name="services">The service collection of the engine host or node.</param>
    public override void Initialize(IServiceCollection services)
    {
        services.AddVariable<WitVariableSmtTaskPackage>();
        services.AddVariable<WitVariableSmtResultPackage>();
        services.AddVariable<WitVariableSmtRuntimeDiagnostics>();
        services.AddVariable<WitVariableSmtOptions>();
        services.AddVariable<WitVariableSmtPreflight>();

        services.AddCollection<WitVariableSmtTaskPackageCollection>();
        services.AddCollection<WitVariableSmtResultPackageCollection>();

        services.AddActivityAdapter<WitActivitySmtSolvePackage, WitActivityAdapterSmtSolvePackage>();
        services.AddActivityAdapter<WitActivitySmtRuntimeDiagnostics, WitActivityAdapterSmtRuntimeDiagnostics>();
        services.AddActivityAdapter<WitActivitySmtCapabilities, WitActivityAdapterSmtCapabilities>();
        services.AddActivityAdapter<WitActivitySmtSplit, WitActivityAdapterSmtSplit>();
        services.AddActivityAdapter<WitActivitySmtSamplingRate, WitActivityAdapterSmtSamplingRate>();
        services.AddActivityAdapter<WitActivitySmtRepairs, WitActivityAdapterSmtRepairs>();
        services.AddActivityAdapter<WitActivitySmtDisputes, WitActivityAdapterSmtDisputes>();
        services.AddActivityAdapter<WitActivitySmtSettle, WitActivityAdapterSmtSettle>();
        services.AddActivityAdapter<WitActivitySmtCollect, WitActivityAdapterSmtCollect>();
        services.AddActivityAdapter<WitActivitySmtCollectResults, WitActivityAdapterSmtCollectResults>();
        services.AddActivityAdapter<WitActivitySmtProvenance, WitActivityAdapterSmtProvenance>();
        services.AddActivityAdapter<WitActivitySmtIntegrity, WitActivityAdapterSmtIntegrity>();
        services.AddActivityAdapter<WitActivitySmtPreflight, WitActivityAdapterSmtPreflight>();
        services.AddActivityAdapter<WitActivitySmtForget, WitActivityAdapterSmtForget>();
    }

    #endregion
}
