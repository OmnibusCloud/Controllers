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

        // Loads rowMatrix.smat via the adapter's own resource resolver,
        // which expects <assembly-dir>/Resources/<file>. Under the closed
        // engine that path resolves through the controller-loader; the
        // SDK loads modules with a different working dir, so the file is
        // not visible at the expected location. Ignored until the SDK's
        // module-resource-resolution path matches the full engine's.
        [Test, Ignore("SDK module-resource path resolution differs from full engine; asset not found.")]
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
