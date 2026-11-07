using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Tests.Mock
{
    internal class MockActivityNode : IWitEngineActivityNode
    {
        public MockActivityNode(IWitEngineNodeBase node, double rate = 0, Guid? nodeId = null, long iterations = 0)
        {
            NodeId = nodeId ?? node.Id;
            BenchmarkResult = rate > 0
                ? new WitBenchmarkResult { Rate = rate, Iterations = iterations }
                : WitBenchmarkResult.Default;
        }

        public Guid NodeId { get; }

        public IWitBenchmarkResult BenchmarkResult { get; }
    }
}
