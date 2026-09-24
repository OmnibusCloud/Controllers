using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Tests.Mock;

/// <summary>
/// One mock "node" of the in-process trio. Each instance carries its OWN id:
/// an allocator that keys anything on the node id must see three nodes, not
/// one node three times.
/// </summary>
internal sealed class FoamTestActivityNode : IWitEngineActivityNode
{
    #region Properties

    public Guid NodeId { get; } = Guid.NewGuid();

    public IWitBenchmarkResult BenchmarkResult => WitBenchmarkResult.Default;

    #endregion
}
