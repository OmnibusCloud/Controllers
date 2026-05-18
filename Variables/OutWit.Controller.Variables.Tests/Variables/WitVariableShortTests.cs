using OutWit.Common.Utils;
using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using System.Linq;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Utils;

namespace OutWit.Controller.Variables.Tests.Variables
{
    [TestFixture]
    public class WitVariableShortTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var variable = new WitVariableShort("myShort");
            Assert.That(variable.Name, Is.EqualTo("myShort"));
            Assert.That(variable.Value, Is.EqualTo(0));
            Assert.That(variable.ToString(), Is.EqualTo("Short:myShort;"));

            variable = new WitVariableShort("myShort", 23);
            Assert.That(variable.Name, Is.EqualTo("myShort"));
            Assert.That(variable.Value, Is.EqualTo(23));
            Assert.That(variable.ToString(), Is.EqualTo("Short:myShort = 23;"));
        }

        [Test]
        public void IsTest()
        {
            var variable = new WitVariableShort("myShort", 23);

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue((short)24)));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myShort2")));
        }

        [Test]
        public void CloneTest()
        {
            var variable = new WitVariableShort("myShort", 23);

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myShort"));
            Assert.That(clone.Value, Is.EqualTo(23));
            
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var variable = new WitVariableShort("myShort", 23);

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myShort"));
            Assert.That(clone.Value, Is.EqualTo(23));

        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             Short:myShort;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableShort("myShort")));
        }
    }
}