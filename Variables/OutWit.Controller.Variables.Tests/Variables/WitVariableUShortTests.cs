using System.Linq;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Utils;

namespace OutWit.Controller.Variables.Tests.Variables
{
    [TestFixture]
    public class WitVariableUShortTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var variable = new WitVariableUShort("myUShort");
            Assert.That(variable.Name, Is.EqualTo("myUShort"));
            Assert.That(variable.Value, Is.EqualTo(0));
            Assert.That(variable.ToString(), Is.EqualTo("UShort:myUShort;"));

            variable = new WitVariableUShort("myUShort", 23);
            Assert.That(variable.Name, Is.EqualTo("myUShort"));
            Assert.That(variable.Value, Is.EqualTo(23));
            Assert.That(variable.ToString(), Is.EqualTo("UShort:myUShort = 23;"));
        }

        [Test]
        public void IsTest()
        {
            var variable = new WitVariableUShort("myUShort", 23);

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue((ushort)24)));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myUShort2")));
        }

        [Test]
        public void CloneTest()
        {
            var variable = new WitVariableUShort("myUShort", 23);

            var clone = variable.Clone();
            
            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myUShort"));
            Assert.That(clone.Value, Is.EqualTo(23));
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var variable = new WitVariableUShort("myUShort", 23);

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myUShort"));
            Assert.That(clone.Value, Is.EqualTo(23));
        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             UShort:myUShort;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableUShort("myUShort")));
        }
    }
}