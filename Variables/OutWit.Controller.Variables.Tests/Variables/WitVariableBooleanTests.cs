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
    public class WitVariableBooleanTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var variable = new WitVariableBoolean("myBool");
            Assert.That(variable.Name, Is.EqualTo("myBool"));
            Assert.That(variable.Value, Is.EqualTo(false));
            Assert.That(variable.ToString(), Is.EqualTo("Bool:myBool;"));

            variable = new WitVariableBoolean("myBool", true);
            Assert.That(variable.Name, Is.EqualTo("myBool"));
            Assert.That(variable.Value, Is.EqualTo(true));
            Assert.That(variable.ToString(), Is.EqualTo("Bool:myBool = True;"));
        }

        [Test]
        public void IsTest()
        {
            var variable = new WitVariableBoolean("myBool", true);

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue(false)));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myBool2")));
        }

        [Test]
        public void CloneTest()
        {
            var variable = new WitVariableBoolean("myBool", true);

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myBool"));
            Assert.That(clone.Value, Is.EqualTo(true));
            
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var variable = new WitVariableBoolean("myBool", true);

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myBool"));
            Assert.That(clone.Value, Is.EqualTo(true));

        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             Bool:myBool;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableBoolean("myBool")));
        }
    }
}