using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>A node of the in-process rig: an id of its own and the default (unmeasured) score.</summary>
internal sealed class SmtTestActivityNode : IWitEngineActivityNode
{
    #region Properties

    /// <inheritdoc />
    public Guid NodeId { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public IWitBenchmarkResult BenchmarkResult => WitBenchmarkResult.Default;

    #endregion
}
