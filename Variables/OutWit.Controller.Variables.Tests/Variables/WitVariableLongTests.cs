using OutWit.Common.Exceptions;
using OutWit.Common.NUnit;
using OutWit.Common.Utils;
using OutWit.Controller.Variables.Adapters;
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
    public class WitVariableLongTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var variable = new WitVariableLong("myLong");
            Assert.That(variable.Name, Is.EqualTo("myLong"));
            Assert.That(variable.Value, Is.EqualTo(0));
            Assert.That(variable.ToString(), Is.EqualTo("Long:myLong;"));

            variable = new WitVariableLong("myLong", 23);
            Assert.That(variable.Name, Is.EqualTo("myLong"));
            Assert.That(variable.Value, Is.EqualTo(23));
            Assert.That(variable.ToString(), Is.EqualTo("Long:myLong = 23;"));
        }

        [Test]
        public void IsTest()
        {
            var variable = new WitVariableLong("myLong", 23);

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue((long)24)));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myLong2")));
        }


        [Test]
        public void CloneTest()
        {
            var variable = new WitVariableLong("myLong", 23);

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myLong"));
            Assert.That(clone.Value, Is.EqualTo(23));
            
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var variable = new WitVariableLong("myLong", 23);

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myLong"));
            Assert.That(clone.Value, Is.EqualTo(23));

        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             Long:myLong;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableLong("myLong")));
        }
    }
}