using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

internal sealed class CorpusTestActivityNode : IWitEngineActivityNode
{
    public CorpusTestActivityNode(IWitEngineNodeBase node)
    {
        NodeId = node.Id;
    }

    public Guid NodeId { get; }

    public IWitBenchmarkResult BenchmarkResult => WitBenchmarkResult.Default;
}
