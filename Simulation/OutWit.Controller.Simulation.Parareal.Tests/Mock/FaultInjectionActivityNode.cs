using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Tests.Mock;

internal sealed class FaultInjectionActivityNode : IWitEngineActivityNode
{
    #region Constructors

    public FaultInjectionActivityNode(Guid nodeId)
    {
        NodeId = nodeId;
    }

    #endregion

    #region Properties

    public Guid NodeId { get; }

    public IWitBenchmarkResult BenchmarkResult => WitBenchmarkResult.Default;

    #endregion
}
