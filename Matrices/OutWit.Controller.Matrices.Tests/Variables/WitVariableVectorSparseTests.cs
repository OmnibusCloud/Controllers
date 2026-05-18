using OutWit.Common.Exceptions;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Matrices.Model;
using OutWit.Controller.Matrices.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Utils;
using System.Linq;
using System.Numerics;
using System.Runtime.Intrinsics;

namespace OutWit.Controller.Matrices.Tests.Variables
{
    [TestFixture]
    public class WitVariableVectorSparseTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var elements = new[] { (1, 1.1), (3, 3.3) };
            var vector = WitVectorSparse<double>.Create(5, elements);
            
            var variable = new WitVariableVectorSparse("myVectorSparse");
            Assert.That(variable.Name, Is.EqualTo("myVectorSparse"));
            Assert.That(variable.Value, Was.EqualTo(null));
            Assert.That(variable.ToString(), Is.EqualTo("VectorSparse:myVectorSparse;"));

            variable = new WitVariableVectorSparse("myVectorSparse", vector);
            Assert.That(variable.Name, Is.EqualTo("myVectorSparse"));
            Assert.That(variable.Value, Is.EqualTo(vector));
        }

        [Test]
        public void IsTest()
        {
            var elements = new[] { (1, 1.1), (3, 3.3) };
            var vector = WitVectorSparse<double>.Create(5, elements);
            
            var variable = new WitVariableVectorSparse("myVectorSparse", vector);

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue((WitVectorSparse<double>.Create(2, [])))));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myVectorSparse2")));
        }


        [Test]
        public void CloneTest()
        {
            var elements = new[] { (1, 1.1), (3, 3.3) };
            var vector = WitVectorSparse<double>.Create(5, elements);
            
            var variable = new WitVariableVectorSparse("myVectorSparse", vector);

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myVectorSparse"));
            Assert.That(clone.Value, Is.EqualTo(vector));
            
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var elements = new[] { (1, 1.1), (3, 3.3) };
            var vector = WitVectorSparse<double>.Create(5, elements);
            
            var variable = new WitVariableVectorSparse("myVectorSparse", vector);

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myVectorSparse"));
            Assert.That(clone.Value, Is.EqualTo(vector));

        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             VectorSparse:myVectorSparse;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableVectorSparse("myVectorSparse")));
        }
    }
}