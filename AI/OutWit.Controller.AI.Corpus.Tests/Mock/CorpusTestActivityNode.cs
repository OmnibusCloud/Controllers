using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

internal sealed class CorpusTestActivityNode : IWitEngineActivityNode
{
    public CorpusTestActivityNode(IWitEngineNodeBase node)
    {
        // A machine of its own: every mock node runs in one process, but copies of a shard
        // are placed by this id, and three nodes with one id would be a single machine.
        NodeId = Guid.NewGuid();
    }

    public Guid NodeId { get; }

    public IWitBenchmarkResult BenchmarkResult => WitBenchmarkResult.Default;
}
