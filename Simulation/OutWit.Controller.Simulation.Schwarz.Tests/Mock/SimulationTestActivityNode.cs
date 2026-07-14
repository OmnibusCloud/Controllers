using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Tests.Mock;

internal sealed class SimulationTestActivityNode : IWitEngineActivityNode
{
    #region Constructors

    public SimulationTestActivityNode(IWitEngineNodeBase node)
    {
        NodeId = node.Id;
    }

    #endregion

    #region Properties

    public Guid NodeId { get; }

    public IWitBenchmarkResult BenchmarkResult => WitBenchmarkResult.Default;

    #endregion
}
