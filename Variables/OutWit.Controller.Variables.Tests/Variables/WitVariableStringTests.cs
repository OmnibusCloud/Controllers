using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using System.Linq;
using OutWit.Common.MemoryPack;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Utils;

namespace OutWit.Controller.Variables.Tests.Variables
{
    [TestFixture]
    public class WitVariableStringTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var variable = new WitVariableString("myString");
            Assert.That(variable.Name, Is.EqualTo("myString"));
            Assert.That(variable.Value, Is.EqualTo(null));
            Assert.That(variable.ToString(), Is.EqualTo("String:myString;"));

            variable = new WitVariableString("myString", "text");
            Assert.That(variable.Name, Is.EqualTo("myString"));
            Assert.That(variable.Value, Is.EqualTo("text"));
            Assert.That(variable.ToString(), Is.EqualTo("String:myString = \"text\";"));
        }

        [Test]
        public void IsTest()
        {
            var variable = new WitVariableString("myString", "text");

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue("text1")));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myString2")));
        }

        [Test]
        public void CloneTest()
        {
            var variable = new WitVariableString("myString", "text");

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myString"));
            Assert.That(clone.Value, Is.EqualTo("text"));
            
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var variable = new WitVariableString("myString", "text");

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myString"));
            Assert.That(clone.Value, Is.EqualTo("text"));

        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             String:myString;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableString("myString")));
        }
    }
}