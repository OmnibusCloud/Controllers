using System;
using System.Collections.Generic;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Variables.Collections;
using OutWit.Engine.Data.Serialization;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Data.Variables;

namespace OutWit.Controller.Variables.Tests.Collections
{
    [TestFixture]
    public class WitVariableDateTimeOffsetCollectionTests
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            SerializationCore.EnsureRegistered();
        }
        
        [Test]
        public void ConstructorsTest()
        {
            WitVariableDateTimeOffsetCollection variable = new WitVariableDateTimeOffsetCollection("variable");
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value, Is.Empty);
            Assert.That(variable.ToString(), Is.EqualTo("DateTimeOffsetCollection:variable = [];"));

            variable = new WitVariableDateTimeOffsetCollection("variable", [new DateTime(2025, 07, 18), new DateTime(2025, 07, 18, 15, 43, 00)]);
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value, Is.EqualTo([new DateTimeOffset(new DateTime(2025, 07, 18)), new DateTimeOffset(new DateTime(2025, 07, 18, 15, 43, 00))]));
        }

        [Test]
        public void IsTest()
        {
            WitVariableDateTimeOffsetCollection variable = new WitVariableDateTimeOffsetCollection("variable", [new DateTime(2025, 07, 18), new DateTime(2025, 07, 18, 15, 43, 00)]);

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue<WitVariableDateTimeOffsetCollection, IReadOnlyList<DateTimeOffset?>>([new DateTimeOffset(new DateTime(2025, 07, 18))])));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("variable1")));
        }

        [Test]
        public void CloneTest()
        {
            WitVariableDateTimeOffsetCollection variable = new WitVariableDateTimeOffsetCollection("variable", [new DateTime(2025, 07, 18), new DateTime(2025, 07, 18, 15, 43, 00)]);

            WitVariableDateTimeOffsetCollection clone = variable.Clone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value, Is.EqualTo(variable.Value));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            WitVariableDateTimeOffsetCollection variable = new WitVariableDateTimeOffsetCollection("variable", [new DateTime(2025, 07, 18), new DateTime(2025, 07, 18, 15, 43, 00)]);

            WitVariableDateTimeOffsetCollection clone = variable.MemoryPackClone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value, Is.EqualTo(variable.Value));
        }
    }
}
