using System;
using System.Linq;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Engine.Data.References;
using OutWit.Common.Utils;
using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Utils;

namespace OutWit.Controller.Variables.Tests.Variables
{
    [TestFixture]
    public class WitVariableTimeSpanTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var variable = new WitVariableTimeSpan("myTimeSpan");
            Assert.That(variable.Name, Is.EqualTo("myTimeSpan"));
            Assert.That(variable.Value, Is.EqualTo(null));
            Assert.That(variable.ToString(), Is.EqualTo("TimeSpan:myTimeSpan;"));

            variable = new WitVariableTimeSpan("myTimeSpan", new TimeSpan(1, 2, 3));
            Assert.That(variable.Name, Is.EqualTo("myTimeSpan"));
            Assert.That(variable.Value, Is.EqualTo(new TimeSpan(1, 2, 3)));
            Assert.That(variable.ToString(), Is.EqualTo("TimeSpan:myTimeSpan = \"01:02:03\";"));

        }

        [Test]
        public void IsTest()
        {
            var variable = new WitVariableTimeSpan("myTimeSpan", new TimeSpan(1, 2, 3));

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue<WitVariableTimeSpan, TimeSpan?>(new TimeSpan(1, 2, 4))));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myTimeSpan2")));
        }

        [Test]
        public void CloneTest()
        {
            var variable = new WitVariableTimeSpan("myTimeSpan", new TimeSpan(1, 2, 3));

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myTimeSpan"));
            Assert.That(clone.Value, Is.EqualTo(new TimeSpan(1, 2, 3)));
            
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var variable = new WitVariableTimeSpan("myTimeSpan", new TimeSpan(1, 2, 3));

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myTimeSpan"));
            Assert.That(clone.Value, Is.EqualTo(new TimeSpan(1, 2, 3)));

        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             TimeSpan:myTimeSpan;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableTimeSpan("myTimeSpan")));
        }
    }
}