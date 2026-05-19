using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Tests.Mock;

internal sealed class RenderDccTestActivityNode : IWitEngineActivityNode
{
    #region Constructors

    public RenderDccTestActivityNode(IWitEngineNodeBase node)
    {
        NodeId = node.Id;
    }

    #endregion

    #region Properties

    public Guid NodeId { get; }

    public IWitBenchmarkResult BenchmarkResult => WitBenchmarkResult.Default;

    #endregion
}
