using OutWit.Common.Utils;
using OutWit.Controller.Variables.Activities;
using OutWit.Controller.Variables.Model;
using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Interfaces;
using System.Linq;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Utils;

namespace OutWit.Controller.Variables.Tests.Variables
{
    [TestFixture]
    public class WitVariableColorTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var variable = new WitVariableColor("myColor");
            Assert.That(variable.Name, Is.EqualTo("myColor"));
            Assert.That(variable.Value, Is.EqualTo(null));
            Assert.That(variable.ToString(), Is.EqualTo("Color:myColor;"));

            variable = new WitVariableColor("myColor", new WitColor(255, 255 ,255));
            Assert.That(variable.Name, Is.EqualTo("myColor"));
            Assert.That(variable.Value, Was.EqualTo(new WitColor(255, 255, 255)));
            Assert.That(variable.ToString(), Is.EqualTo("Color:myColor = \"#FFFFFFFF\";"));
        }

        [Test]
        public void IsTest()
        {
            var variable = new WitVariableColor("myColor", new WitColor(255, 255, 255));

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue(new WitColor(255, 255, 0))));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myColor2")));
        }

        [Test]
        public void CloneTest()
        {
            var variable = new WitVariableColor("myColor", new WitColor(255, 255, 255));

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myColor"));
            Assert.That(clone.Value, Was.EqualTo(new WitColor(255, 255, 255)));
            
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var variable = new WitVariableColor("myColor", new WitColor(255, 255, 255));

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myColor"));
            Assert.That(clone.Value, Was.EqualTo(new WitColor(255, 255, 255)));

        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             Color:myColor;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableColor("myColor")));
        }
    }
}