using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Mock;

internal sealed class VerifyTestActivityNode : IWitEngineActivityNode
{
    public VerifyTestActivityNode(IWitEngineNodeBase node)
    {
        NodeId = node.Id;
    }

    public Guid NodeId { get; }

    public IWitBenchmarkResult BenchmarkResult => WitBenchmarkResult.Default;
}
