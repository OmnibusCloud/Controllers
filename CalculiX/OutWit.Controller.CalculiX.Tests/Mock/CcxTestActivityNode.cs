using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.CalculiX.Tests.Mock;

/// <summary>
/// One mock "node" of the in-process pair, with its own id.
/// </summary>
internal sealed class CcxTestActivityNode : IWitEngineActivityNode
{
    #region Properties

    public Guid NodeId { get; } = Guid.NewGuid();

    public IWitBenchmarkResult BenchmarkResult => WitBenchmarkResult.Default;

    #endregion
}
