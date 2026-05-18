using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Matrices.Tests.Mock
{
    internal class MockActivityNode : IWitEngineActivityNode
    {
        public MockActivityNode(IWitEngineNodeBase node)
        {
            NodeId = node.Id;
        }
        public Guid NodeId { get; }
        public IWitBenchmarkResult BenchmarkResult => WitBenchmarkResult.Default;
    }
}
