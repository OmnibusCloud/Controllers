using OutWit.Common.Exceptions;
using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Matrices.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using System.Linq;
using System.Numerics;
using OutWit.Common.MemoryPack;
using OutWit.Controller.Matrices.Model;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Utils;

namespace OutWit.Controller.Matrices.Tests.Variables
{
    [TestFixture]
    public class WitVariableVectorTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var variable = new WitVariableVector("myVector");
            Assert.That(variable.Name, Is.EqualTo("myVector"));
            Assert.That(variable.Value, Was.EqualTo(null));
            Assert.That(variable.ToString(), Is.EqualTo("Vector:myVector;"));

            variable = new WitVariableVector("myVector", new [] { 1.1, 2.2, 3.3 });
            Assert.That(variable.Name, Is.EqualTo("myVector"));
            Assert.That(variable.Value, Is.EqualTo(new[] { 1.1, 2.2, 3.3 }));
            Assert.That(variable.ToString(), Is.EqualTo("Vector:myVector = [1.1, 2.2, 3.3];"));
        }

        [Test]
        public void IsTest()
        {
            var variable = new WitVariableVector("myVector", new[] { 1.1, 2.2, 3.3 });

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue((WitVector<double>)new[] { 1.1, 2.2})));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myVector2")));
        }


        [Test]
        public void CloneTest()
        {
            var variable = new WitVariableVector("myVector", new[] { 1.1, 2.2, 3.3 });

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myVector"));
            Assert.That(clone.Value, Is.EqualTo(new[] { 1.1, 2.2, 3.3 }));
            
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var variable = new WitVariableVector("myVector", new[] { 1.1, 2.2, 3.3 });

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myVector"));
            Assert.That(clone.Value, Is.EqualTo(new[] { 1.1, 2.2, 3.3 }));

        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             Vector:myVector;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableVector("myVector")));
        }
    }
}