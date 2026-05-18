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
    public class WitVariableFloatTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var variable = new WitVariableFloat("myFloat");
            Assert.That(variable.Name, Is.EqualTo("myFloat"));
            Assert.That(variable.Value, Is.EqualTo(0.0));
            Assert.That(variable.ToString(), Is.EqualTo("Float:myFloat;"));

            variable = new WitVariableFloat("myFloat", 23.4f);
            Assert.That(variable.Name, Is.EqualTo("myFloat"));
            Assert.That(variable.Value, Is.EqualTo(23.4f));
            Assert.That(variable.ToString(), Is.EqualTo("Float:myFloat = 23.4;"));
        }

        [Test]
        public void IsTest()
        {
            var variable = new WitVariableFloat("myFloat", 23.4f);

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue(24.5f)));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myFloat2")));
        }


        [Test]
        public void CloneTest()
        {
            var variable = new WitVariableFloat("myFloat", 23.4f);

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myFloat"));
            Assert.That(clone.Value, Is.EqualTo(23.4f));
            
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var variable = new WitVariableFloat("myFloat", 23.4f);

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myFloat"));
            Assert.That(clone.Value, Is.EqualTo(23.4f));

        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             Float:myFloat;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableFloat("myFloat")));
        }
    }
}