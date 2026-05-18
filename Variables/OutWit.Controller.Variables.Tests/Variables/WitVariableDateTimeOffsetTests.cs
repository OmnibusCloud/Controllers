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
    public class WitVariableDateTimeOffsetTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            WitEngineSdk.Instance.Reload(false);
        }

        [Test]
        public void ConstructorsTest()
        {
            var variable = new WitVariableDateTimeOffset("myDateTimeOffset");
            Assert.That(variable.Name, Is.EqualTo("myDateTimeOffset"));
            Assert.That(variable.Value, Is.EqualTo(null));
            Assert.That(variable.ToString(), Is.EqualTo("DateTimeOffset:myDateTimeOffset;"));

            variable = new WitVariableDateTimeOffset("myDateTimeOffset", new DateTimeOffset(new DateTime(2024, 07, 21)));
            Assert.That(variable.Name, Is.EqualTo("myDateTimeOffset"));
            Assert.That(variable.Value, Is.EqualTo(new DateTimeOffset(new DateTime(2024, 07, 21))));
            Assert.That(variable.ToString(), Is.EqualTo($"DateTimeOffset:myDateTimeOffset = \"{new DateTimeOffset(new DateTime(2024, 07, 21))}\";"));

        }

        [Test]
        public void IsTest()
        {
            var variable = new WitVariableDateTimeOffset("myDateTimeOffset", new DateTimeOffset(new DateTime(2024, 07, 21)));
            
            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue<WitVariableDateTimeOffset, DateTimeOffset?>(new DateTimeOffset(new DateTime(2024, 07, 22)))));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("myDateTimeOffset2")));
        }

        [Test]
        public void CloneTest()
        {
            var variable = new WitVariableDateTimeOffset("myDateTimeOffset", new DateTimeOffset(new DateTime(2024, 07, 21)));

            var clone = variable.Clone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myDateTimeOffset"));
            Assert.That(clone.Value, Is.EqualTo(new DateTimeOffset(new DateTime(2024, 07, 21))));
            
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            var variable = new WitVariableDateTimeOffset("myDateTimeOffset", new DateTimeOffset(new DateTime(2024, 07, 21)));

            var clone = variable.MemoryPackClone();

            Assert.That(clone, Was.EqualTo(variable));
            Assert.That(clone.Name, Is.EqualTo("myDateTimeOffset"));
            Assert.That(clone.Value, Is.EqualTo(new DateTimeOffset(new DateTime(2024, 07, 21))));

        }

        [Test]
        public void ParseVariable()
        {
            var script = """
                         Job:TestJob()
                         {
                             DateTimeOffset:myDateTimeOffset;
                         }
                         """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Activities, Was.Empty);
            Assert.That(job.Variables.Count, Was.EqualTo(1));
            Assert.That(job.Variables.Single(), Was.EqualTo(new WitVariableDateTimeOffset("myDateTimeOffset")));
        }
    }
}