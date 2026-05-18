using OutWit.Common.Utils;
using OutWit.Controller.Variables.Variables;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.Exceptions;
using System;
using System.Linq;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Utils;

namespace OutWit.Controller.Variables.Tests.Variables
{
    [TestFixture]
    public class WitVariableDateTimeTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var variable = new WitVariableDateTime("myDateTime");
            Assert.That(variable.Name, Is.EqualTo("myDateTime"));
            Assert.That(variable.Value, Is.EqualTo(null));
            Assert.That(variable.ToString(), Is.EqualTo("DateTime:myDateTime;"));

            variable = new WitVariableDateTime("myDateTime", new DateTime(2024, 07, 21));
            Assert.That(variable.Name, Is.EqualTo("myDateTime"));
            Assert.That(variable.Value, Is.EqualTo(new DateTime(2024, 07, 21)));
            Assert.That(variable.ToString(), Is.EqualTo($"DateTime:myDateTime = \"{new DateTime(2024, 07, 21):G}\";"));
        }

        [Test]
        public void IsTest()
        {
            var variable = new WitVariableDateTime("myDateTime", new DateTime(2024, 07, 21));

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue<WitVariableDateTime, DateTime?>(new DateTime(2024, 07, 22))));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myDateTime2")));
        }

        [Test]
        public void CloneTest()
        {
            var variable = new WitVariableDateTime("myDateTime", new DateTime(2024, 07, 21));

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myDateTime"));
            Assert.That(clone.Value, Is.EqualTo(new DateTime(2024, 07, 21)));
            
        }

        [Test]
        public void MemoryPackCloneTest()
        {
            var variable = new WitVariableDateTime("myDateTime", new DateTime(2024, 07, 21));

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myDateTime"));
            Assert.That(clone.Value, Is.EqualTo(new DateTime(2024, 07, 21)));

        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             DateTime:myDateTime;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableDateTime("myDateTime")));
        }
    }
}