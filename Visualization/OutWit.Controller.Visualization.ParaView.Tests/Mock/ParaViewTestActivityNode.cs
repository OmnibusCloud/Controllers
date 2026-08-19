using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Tests.Mock;

internal sealed class ParaViewTestActivityNode : IWitEngineActivityNode
{
    #region Constructors

    public ParaViewTestActivityNode(IWitEngineNodeBase node)
    {
        NodeId = node.Id;
    }

    #endregion

    #region Properties

    public Guid NodeId { get; }

    public IWitBenchmarkResult BenchmarkResult => WitBenchmarkResult.Default;

    #endregion
}
