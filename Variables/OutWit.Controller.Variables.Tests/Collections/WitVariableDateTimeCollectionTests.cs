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
    public class WitVariableDateTimeCollectionTests
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            SerializationCore.EnsureRegistered();
        }
        
        [Test]
        public void ConstructorsTest()
        {
            WitVariableDateTimeCollection variable = new WitVariableDateTimeCollection("variable");
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value, Is.Empty);
            Assert.That(variable.ToString(), Is.EqualTo("DateTimeCollection:variable = [];"));

            variable = new WitVariableDateTimeCollection("variable", [new DateTime(2025, 07, 18), new DateTime(2025, 07, 18, 15, 43, 00)]);
            Assert.That(variable.Name, Is.EqualTo("variable"));
            Assert.That(variable.Value, Is.EqualTo([new DateTime(2025, 07, 18), new DateTime(2025, 07, 18, 15, 43, 00)]));
        }

        [Test]
        public void IsTest()
        {
            WitVariableDateTimeCollection variable = new WitVariableDateTimeCollection("variable", [new DateTime(2025, 07, 18), new DateTime(2025, 07, 18, 15, 43, 00)]);

            Assert.That(variable, Was.EqualTo(variable.Clone()));
            Assert.That(variable, Was.Not.EqualTo(variable.WithValue<WitVariableDateTimeCollection, IReadOnlyList<DateTime?>>([new DateTime(2025, 07, 18)])));
            Assert.That(variable, Was.Not.EqualTo(variable.WithName("variable1")));
        }

        [Test]
        public void CloneTest()
        {
            WitVariableDateTimeCollection variable = new WitVariableDateTimeCollection("variable", [new DateTime(2025, 07, 18), new DateTime(2025, 07, 18, 15, 43, 00)]);

            WitVariableDateTimeCollection clone = variable.Clone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value, Is.EqualTo(variable.Value));
        }
        
        [Test]
        public void MemoryPackCloneTest()
        {
            WitVariableDateTimeCollection variable = new WitVariableDateTimeCollection("variable", [new DateTime(2025, 07, 18), new DateTime(2025, 07, 18, 15, 43, 00)]);

            WitVariableDateTimeCollection clone = variable.MemoryPackClone();
            Assert.That(clone.Name, Was.EqualTo(variable.Name));
            Assert.That(clone.Value, Is.EqualTo(variable.Value));
        }
    }
}
