using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Tests.Mock;

internal sealed class SweepTestActivityNode : IWitEngineActivityNode
{
    #region Constructors

    public SweepTestActivityNode(IWitEngineNodeBase node)
    {
        NodeId = node.Id;
    }

    #endregion

    #region Properties

    public Guid NodeId { get; }

    public IWitBenchmarkResult BenchmarkResult => WitBenchmarkResult.Default;

    #endregion
}
