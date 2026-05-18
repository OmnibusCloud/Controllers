using OutWit.Controller.Matrices.Activities;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Benchmark;

namespace OutWit.Controller.Matrices.Tests.Benchmark
{
    [TestFixture]
    public class BenchmarkTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineNodeSdk.Instance.Reload(false);
        }

        [Test]
        public async Task SparseGustavsonBenchmarkTest()
        {
            var result =
                await WitEngineNodeSdk.Instance.RunBenchmark<WitActivityMatrixGustavsonMultiply>(WitBenchmarkOptions.Default);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Rate, Is.GreaterThan(0));
            Assert.That(result.Iterations, Is.GreaterThan(0));
            Assert.That(result.Elapsed, Is.GreaterThanOrEqualTo(WitBenchmarkOptions.Default.MinDuration));

            Console.WriteLine(result);
        }
    }
}
