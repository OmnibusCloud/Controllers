using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Tests.Mock;

/// <summary>
/// One mock "node" of the in-process trio. Each instance carries its OWN id —
/// an allocator that keys anything on the node id must see three nodes, not
/// one node three times (the fault-injection manager relies on exactly that
/// to single out its victim).
/// </summary>
internal sealed class SweepTestActivityNode : IWitEngineActivityNode
{
    #region Properties

    public Guid NodeId { get; } = Guid.NewGuid();

    public IWitBenchmarkResult BenchmarkResult => WitBenchmarkResult.Default;

    #endregion
}
