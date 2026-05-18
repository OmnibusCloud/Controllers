using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Tests.Mock;

internal sealed class RenderTestActivityNode : IWitEngineActivityNode
{
    #region Constructors

    public RenderTestActivityNode(IWitEngineNodeBase node)
    {
        NodeId = node.Id;
    }

    #endregion

    #region Properties

    public Guid NodeId { get; }

    public IWitBenchmarkResult BenchmarkResult => WitBenchmarkResult.Default;

    #endregion
}
